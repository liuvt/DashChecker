using System.Globalization;
using Microsoft.Extensions.Options;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class GoogleShiftService
{
    private static readonly string[] ExpectedHeaders =
    [
        "thoi_gian_tao", "so_tai", "so_cho", "bien_kiem_soat", "hoten_msnv",
        "sdt_laixe", "trangthai_len_xuong_ca", "loaihinh_hoptac", "hinhthuc_kinhdoanh",
        "ly_do_xuong_ca", "ghi_chu", "thoi_gian", "hinhthuc_luong"
    ];

    private readonly HttpClient _httpClient;
    private readonly GoogleShiftOptions _options;

    public GoogleShiftService(HttpClient httpClient, IOptions<GoogleShiftOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 30, 600));
    }

    public async Task<ParsedShiftReport> FetchCurrentAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        var spreadsheetId = _options.SpreadsheetId.Trim();
        if (string.IsNullOrWhiteSpace(spreadsheetId))
            throw new InvalidOperationException("Chưa cấu hình GoogleShift:SpreadsheetId.");

        var url = $"https://docs.google.com/spreadsheets/d/{Uri.EscapeDataString(spreadsheetId)}/export?format=csv&gid={_options.Gid}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var finalHost = response.RequestMessage?.RequestUri?.Host ?? string.Empty;

        if (!response.IsSuccessStatusCode ||
            mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
            finalHost.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
            body.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            body.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Không đọc được Google Sheet QL_LEN_XUONG_CA. Hãy đặt quyền file là 'Bất kỳ ai có đường liên kết - Người xem' " +
                "hoặc cấu hình nguồn Google Sheets có quyền truy cập cho máy chạy DashChecker.");
        }

        var csvRows = ParseCsv(body);
        if (csvRows.Count < 2)
            throw new InvalidOperationException("Sheet QL_LEN_XUONG_CA không có dữ liệu.");

        var header = csvRows[0];
        var columnMap = ExpectedHeaders.ToDictionary(
            h => h,
            h => Array.FindIndex(header, x => string.Equals(x.Trim(), h, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var missing = columnMap.Where(x => x.Value < 0).Select(x => x.Key).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("QL_LEN_XUONG_CA thiếu cột: " + string.Join(", ", missing));

        var parsed = new List<ParsedShiftAssignment>();

        for (var i = 1; i < csvRows.Count; i++)
        {
            var row = csvRows[i];
            string Cell(string name)
            {
                var index = columnMap[name];
                return index >= 0 && index < row.Length ? row[index].Trim() : string.Empty;
            }

            var sourceDateText = Cell("thoi_gian_tao");
            if (!DateTime.TryParseExact(sourceDateText, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var sourceDate))
                continue;

            var soTai = Cell("so_tai");
            var rawDriver = Cell("hoten_msnv");
            var (driverName, employeeCode) = ParseDriver(rawDriver);

            if (!BelongsToArea(area.AreaCode, soTai, employeeCode))
                continue;

            var timeText = Cell("thoi_gian");
            if (!TimeSpan.TryParseExact(timeText, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var sourceTime))
                sourceTime = TimeSpan.Zero;

            var status = Cell("trangthai_len_xuong_ca");
            var plate = Cell("bien_kiem_soat");
            var phone = NormalizePhone(Cell("sdt_laixe"));
            var isActive = string.Equals(status, "Lên ca", StringComparison.OrdinalIgnoreCase);

            parsed.Add(new ParsedShiftAssignment(
                SourceRow: i + 1,
                SourceDate: sourceDate.Date,
                SoTai: soTai,
                SoCho: Cell("so_cho"),
                BienKiemSoat: plate,
                BienKiemSoatNormalized: VehicleKey.Normalize(plate),
                HoTenMsnv: rawDriver,
                DriverName: driverName,
                EmployeeCode: employeeCode,
                DriverPhone: phone,
                TrangThaiLenXuongCa: status,
                LoaiHinhHopTac: Cell("loaihinh_hoptac"),
                HinhThucKinhDoanh: Cell("hinhthuc_kinhdoanh"),
                LyDoXuongCa: Cell("ly_do_xuong_ca"),
                GhiChu: Cell("ghi_chu"),
                SourceTime: sourceTime,
                SourceAt: BuildOperationalSourceAt(sourceDate.Date, sourceTime),
                HinhThucLuong: Cell("hinhthuc_luong"),
                IsActive: isActive));
        }

        if (parsed.Count == 0)
            throw new InvalidOperationException($"Không tìm thấy dữ liệu khu vực {area.AreaName} trong QL_LEN_XUONG_CA.");

        // Current chỉ lấy snapshot ngày mới nhất có trong sheet của đúng khu vực.
        var latestSourceDate = parsed.Max(x => x.SourceDate).Date;
        var currentRows = parsed
            .Where(x => x.SourceDate.Date == latestSourceDate)
            .OrderBy(x => x.SoTai, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.BienKiemSoat, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceAt)
            .ThenBy(x => x.SourceRow)
            .ToList();

        return new ParsedShiftReport(
            spreadsheetId,
            string.IsNullOrWhiteSpace(_options.SheetName) ? "QL_LEN_XUONG_CA" : _options.SheetName,
            latestSourceDate,
            currentRows);
    }

    private static DateTime BuildOperationalSourceAt(DateTime sourceDate, TimeSpan sourceTime)
        => sourceTime < TimeSpan.FromHours(5)
            ? sourceDate.Date.AddDays(1).Add(sourceTime)
            : sourceDate.Date.Add(sourceTime);

    private static bool BelongsToArea(string areaCode, string soTai, string employeeCode)
    {
        return soTai.StartsWith(areaCode, StringComparison.OrdinalIgnoreCase) ||
               employeeCode.StartsWith(areaCode, StringComparison.OrdinalIgnoreCase);
    }

    private static (string DriverName, string EmployeeCode) ParseDriver(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return (string.Empty, string.Empty);

        var separator = raw.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separator < 0) return (raw, string.Empty);

        return (raw[..separator].Trim(), raw[(separator + 3)..].Trim());
    }

    private static string NormalizePhone(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 9) return "0" + digits;
        return digits;
    }

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row.ToArray());
        }

        return rows;
    }
}
