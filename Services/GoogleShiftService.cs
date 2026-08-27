using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;
using DashChecker.Models;
using DashChecker.Extensions;

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
    private readonly IWebHostEnvironment _environment;
    private SheetsService? _sheetsService;

    public GoogleShiftService(
        HttpClient httpClient,
        IOptions<GoogleShiftOptions> options,
        IWebHostEnvironment environment)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _environment = environment;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 30, 600));
    }

    public async Task<ParsedShiftReport> FetchCurrentAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        ValidateSpreadsheetConfiguration();

        // Nếu đã có Service Account thì đọc bằng API để Sheet có thể để Private.
        // Nếu chưa có thì giữ tương thích với cơ chế CSV public của project cũ.
        if (HasServiceAccountConfiguration())
        {
            var service = GetSheetsService(requireWriteAccess: false);
            var range = $"'{EscapeSheetName(GetSheetName())}'!A:ZZ";
            var values = await service.ltvGetSheetValuesAsync(GetSpreadsheetId(), range, cancellationToken);
            var rows = ToStringRows(values);
            return ParseReport(area, rows);
        }

        return await FetchCurrentViaPublicCsvAsync(area, cancellationToken);
    }

    public async Task<int> AddRowAsync(
        AreaContext area,
        ShiftCurrentEditModel input,
        CancellationToken cancellationToken = default)
    {
        var service = GetSheetsService(requireWriteAccess: true);
        var headerMap = await GetHeaderMapAsync(service, cancellationToken);
        var valuesByHeader = BuildSheetValues(area, input);

        var width = Math.Max(headerMap.Values.Max() + 1, ExpectedHeaders.Length);
        var row = Enumerable.Repeat<object>(string.Empty, width).ToList();
        foreach (var (header, value) in valuesByHeader)
            row[headerMap[header]] = value;

        var body = new ValueRange { Values = new List<IList<object>> { row } };
        var range = $"'{EscapeSheetName(GetSheetName())}'!A:ZZ";
        var sourceRow = await service.ltvAppendSheetValuesAndGetRowAsync(
            GetSpreadsheetId(), range, body, cancellationToken);
        if (sourceRow <= 1)
            throw new InvalidOperationException("Google Sheet đã nhận dữ liệu nhưng không xác định được số dòng vừa thêm. Hãy bấm Cập nhật Current để đồng bộ lại.");

        return sourceRow;
    }

    public async Task UpdateRowAsync(
        AreaContext area,
        int sourceRow,
        ShiftCurrentEditModel input,
        CancellationToken cancellationToken = default)
    {
        if (sourceRow <= 1)
            throw new InvalidOperationException("SourceRow Google Sheet không hợp lệ.");

        var service = GetSheetsService(requireWriteAccess: true);
        var headerMap = await GetHeaderMapAsync(service, cancellationToken);
        var valuesByHeader = BuildSheetValues(area, input);
        var sheetName = EscapeSheetName(GetSheetName());

        var data = new List<ValueRange>();
        foreach (var header in ExpectedHeaders)
        {
            var column = ColumnName(headerMap[header] + 1);
            data.Add(new ValueRange
            {
                Range = $"'{sheetName}'!{column}{sourceRow}",
                Values = new List<IList<object>> { new List<object> { valuesByHeader[header] } }
            });
        }

        await service.ltvBatchUpdateSheetValuesAsync(GetSpreadsheetId(), data, cancellationToken);
    }

    public async Task DeleteRowAsync(
        int sourceRow,
        CancellationToken cancellationToken = default)
    {
        if (sourceRow <= 1)
            throw new InvalidOperationException("Không thể xóa dòng tiêu đề hoặc SourceRow Google Sheet không hợp lệ.");

        var service = GetSheetsService(requireWriteAccess: true);
        await service.DeleteDimensionRequestAsync(
            GetSpreadsheetId(),
            GetSheetName(),
            sourceRow - 1,
            cancellationToken);
    }

    private async Task<ParsedShiftReport> FetchCurrentViaPublicCsvAsync(
        AreaContext area,
        CancellationToken cancellationToken)
    {
        var spreadsheetId = GetSpreadsheetId();
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
                "Không đọc được Google Sheet QL_LEN_XUONG_CA. Hãy cấu hình Service Account hoặc đặt quyền file là 'Bất kỳ ai có đường liên kết - Người xem'.");
        }

        return ParseReport(area, ParseCsv(body));
    }

    private ParsedShiftReport ParseReport(AreaContext area, List<string[]> csvRows)
    {
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
            var parsedSourceDate = sourceDateText.ltvStringToDateTime(CultureInfo.InvariantCulture);
            if (!parsedSourceDate.HasValue)
                continue;
            var sourceDate = parsedSourceDate.Value;

            var soTai = Cell("so_tai");
            var rawDriver = Cell("hoten_msnv");
            var (driverName, employeeCode) = ParseDriver(rawDriver);

            if (!BelongsToArea(area.AreaCode, soTai, employeeCode))
                continue;

            var timeText = Cell("thoi_gian");
            var sourceTime = timeText.ltvStringToTimeSpan() ?? TimeSpan.Zero;

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

        var latestSourceDate = parsed.Max(x => x.SourceDate).Date;
        var currentRows = parsed
            .Where(x => x.SourceDate.Date == latestSourceDate)
            .OrderBy(x => x.SoTai, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.BienKiemSoat, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceAt)
            .ThenBy(x => x.SourceRow)
            .ToList();

        return new ParsedShiftReport(GetSpreadsheetId(), GetSheetName(), latestSourceDate, currentRows);
    }

    private async Task<Dictionary<string, int>> GetHeaderMapAsync(
        SheetsService service,
        CancellationToken cancellationToken)
    {
        var range = $"'{EscapeSheetName(GetSheetName())}'!1:1";
        var request = service.Spreadsheets.Values.Get(GetSpreadsheetId(), range);
        var response = await request.ExecuteAsync(cancellationToken);
        var header = response.Values?.FirstOrDefault()?.Select(x => x?.ToString()?.Trim() ?? string.Empty).ToArray()
                     ?? Array.Empty<string>();

        var map = ExpectedHeaders.ToDictionary(
            h => h,
            h => Array.FindIndex(header, x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var missing = map.Where(x => x.Value < 0).Select(x => x.Key).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("QL_LEN_XUONG_CA thiếu cột: " + string.Join(", ", missing));

        return map;
    }

    private Dictionary<string, string> BuildSheetValues(AreaContext area, ShiftCurrentEditModel input)
    {
        if (input.SourceDate == default)
            throw new InvalidOperationException("Ngày ca không hợp lệ.");
        if (!input.SourceTime.HasValue)
            throw new InvalidOperationException("Vui lòng nhập thời gian lên/xuống ca.");

        var soTai = input.SoTai?.Trim() ?? string.Empty;
        var plate = input.BienKiemSoat?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(soTai) && string.IsNullOrWhiteSpace(plate))
            throw new InvalidOperationException("Cần nhập ít nhất Số tài hoặc Biển kiểm soát.");

        var rawDriver = input.HoTenMsnv?.Trim() ?? string.Empty;
        var (_, employeeCode) = ParseDriver(rawDriver);
        if (!string.IsNullOrWhiteSpace(soTai) || !string.IsNullOrWhiteSpace(employeeCode))
        {
            var belongs = soTai.StartsWith(area.AreaCode, StringComparison.OrdinalIgnoreCase) ||
                          employeeCode.StartsWith(area.AreaCode, StringComparison.OrdinalIgnoreCase);
            if (!belongs)
                throw new InvalidOperationException($"Số tài/MSNV không thuộc khu vực {area.AreaName} ({area.AreaCode}).");
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["thoi_gian_tao"] = input.SourceDate.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["so_tai"] = soTai,
            ["so_cho"] = input.SoCho?.Trim() ?? string.Empty,
            ["bien_kiem_soat"] = plate,
            ["hoten_msnv"] = rawDriver,
            ["sdt_laixe"] = NormalizePhone(input.DriverPhone ?? string.Empty),
            ["trangthai_len_xuong_ca"] = input.TrangThaiLenXuongCa?.Trim() ?? string.Empty,
            ["loaihinh_hoptac"] = input.LoaiHinhHopTac?.Trim() ?? string.Empty,
            ["hinhthuc_kinhdoanh"] = input.HinhThucKinhDoanh?.Trim() ?? string.Empty,
            ["ly_do_xuong_ca"] = input.LyDoXuongCa?.Trim() ?? string.Empty,
            ["ghi_chu"] = input.GhiChu?.Trim() ?? string.Empty,
            ["thoi_gian"] = input.SourceTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["hinhthuc_luong"] = input.HinhThucLuong?.Trim() ?? string.Empty
        };
    }

    private SheetsService GetSheetsService(bool requireWriteAccess)
    {
        if (_sheetsService is not null)
            return _sheetsService;

        if (!HasServiceAccountConfiguration())
        {
            var action = requireWriteAccess ? "Thêm/Sửa/Xóa" : "truy cập";
            throw new InvalidOperationException(
                $"Muốn {action} Google Sheet, hãy cấu hình GoogleShift:ServiceAccountJsonPath hoặc GoogleShift:ServiceAccountJson, " +
                "sau đó chia sẻ file Google Sheet quyền Editor cho email Service Account.");
        }

        GoogleCredential credential;
        if (!string.IsNullOrWhiteSpace(_options.ServiceAccountJson))
        {
            credential = GoogleCredential.FromJson(_options.ServiceAccountJson);
        }
        else
        {
            var path = ResolveCredentialPath(_options.ServiceAccountJsonPath);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Không tìm thấy Google Service Account JSON tại: {path}");
            credential = GoogleCredential.FromFile(path);
        }

        credential = credential.CreateScoped(SheetsService.Scope.Spreadsheets);
        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = string.IsNullOrWhiteSpace(_options.ApplicationName) ? "DashChecker" : _options.ApplicationName
        });

        return _sheetsService;
    }

    private string ResolveCredentialPath(string configuredPath)
    {
        var path = configuredPath.Trim();
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, path));
    }

    private bool HasServiceAccountConfiguration()
        => !string.IsNullOrWhiteSpace(_options.ServiceAccountJson) ||
           !string.IsNullOrWhiteSpace(_options.ServiceAccountJsonPath);

    private void ValidateSpreadsheetConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.SpreadsheetId))
            throw new InvalidOperationException("Chưa cấu hình GoogleShift:SpreadsheetId.");
        if (string.IsNullOrWhiteSpace(_options.SheetName))
            throw new InvalidOperationException("Chưa cấu hình GoogleShift:SheetName.");
    }

    private string GetSpreadsheetId()
    {
        ValidateSpreadsheetConfiguration();
        return _options.SpreadsheetId.Trim();
    }

    private string GetSheetName()
    {
        ValidateSpreadsheetConfiguration();
        return _options.SheetName.Trim();
    }

    private static List<string[]> ToStringRows(IList<IList<object>>? values)
    {
        if (values is null) return new List<string[]>();
        return values.Select(row => row.Select(x => x?.ToString() ?? string.Empty).ToArray()).ToList();
    }

    private static string ColumnName(int oneBasedColumn)
    {
        var value = oneBasedColumn;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static string EscapeSheetName(string name) => name.Replace("'", "''");

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
