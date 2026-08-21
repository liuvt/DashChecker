using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExcelDataReader;
using Microsoft.Extensions.Options;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class SkySoftReportService : IDisposable
{
    private static readonly string[] ExpectedColumns =
    [
        "Số hiệu",
        "Biển số",
        "Bắt đầu",
        "Kết thúc",
        "KM có khách",
        "KM rỗng",
        "Tổng KM",
        "Thành tiền",
        "Điểm đầu",
        "Điểm cuối"
    ];

    private readonly SkySoftOptions _options;
    private readonly CredentialStoreService _credentialStore;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private CookieContainer _cookies = new();
    private HttpClientHandler? _handler;
    private HttpClient? _httpClient;
    private bool _loggedIn;

    public SkySoftReportService(
        IOptions<SkySoftOptions> options,
        CredentialStoreService credentialStore)
    {
        _options = options.Value;
        _credentialStore = credentialStore;
        CreateFreshSession();
    }

    public bool IsLoggedIn => _loggedIn && HasTokenCookie();

    public void Logout()
    {
        _loggedIn = false;
        CreateFreshSession();
    }

    public async Task LoginAsync(
        string userName,
        string password,
        bool rememberPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("Vui lòng nhập tài khoản SkySoft.");

        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Vui lòng nhập mật khẩu SkySoft.");

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            CreateFreshSession();
            _loggedIn = false;

            var deviceId = await _credentialStore.GetOrCreateDeviceIdAsync(cancellationToken);

            // Khởi tạo session/JSESSIONID giống browser trước khi login.
            using (var initRequest = new HttpRequestMessage(HttpMethod.Get, "/"))
            {
                initRequest.Headers.Referrer = new Uri("https://go.skysoft.vn/");
                using var initResponse = await Client.SendAsync(
                    initRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }

            var payload = new SkySoftLoginRequest
            {
                UserName = userName.Trim(),
                Password = password,
                DeviceID = deviceId,
                DeviceName = _options.DeviceName,
                DeviceModel = string.Empty,
                DeviceBrand = "Google Inc.",
                AppOs = "web",
                OsVersion = string.Empty,
                AppVersion = _options.AppVersion,
                FireBaseToken = string.Empty,
                Reconnect = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "rest/win/v2/login")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Referrer = new Uri("https://go.skysoft.vn/");

            using var response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"SkySoft login lỗi HTTP {(int)response.StatusCode}: {Limit(body, 500)}");
            }

            if (!HasTokenCookie())
            {
                throw new InvalidOperationException(
                    "Đăng nhập không thành công: SkySoft không trả cookie tokenID. " +
                    $"Response: {Limit(body, 500)}");
            }

            _loggedIn = true;

            await _credentialStore.SaveAfterSuccessfulLoginAsync(
                userName,
                password,
                rememberPassword,
                deviceId,
                cancellationToken);
        }
        catch
        {
            _loggedIn = false;
            throw;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<ParsedTaxiReport> FetchRecentTaxiTripsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            throw new InvalidOperationException("Bạn chưa đăng nhập SkySoft.");

        // Khoảng báo cáo cố định theo nghiệp vụ:
        // từ 05:00:00 ngày hôm qua đến 05:00:00 ngày hôm nay (giờ Việt Nam).
        var toDate = VietnamClock.Now.Date;
        var fromDate = toDate.AddDays(-1);
        var fromTime = new TimeSpan(5, 0, 0);
        var toTime = new TimeSpan(5, 0, 0);

        try
        {
            // Mỗi lần cập nhật chỉ lấy đúng một kỳ 24 giờ: 05:00 hôm qua → 05:00 hôm nay.
            // SkySoft có thể đóng gói khá lâu nên HttpClient được cấu hình timeout riêng (mặc định
            // 30 phút). Nếu gateway SkySoft chủ động trả 504, chỉ retry POST tạo
            // report; dữ liệu SQLite cũ chưa bị đụng tới ở giai đoạn này.
            var payload = CreateReportPayload(fromDate, toDate, fromTime, toTime);
            var fileId = await ExecuteReportWithRetryAsync(
                payload,
                fromDate,
                toDate,
                cancellationToken);

            var trips = await ReadExcelDetailAsync(fileId, cancellationToken);

            return new ParsedTaxiReport(
                fileId,
                trips,
                fromDate,
                toDate,
                fromTime,
                toTime);
        }
        catch (SkySoftSessionExpiredException)
        {
            _loggedIn = false;
            throw new InvalidOperationException("Phiên SkySoft đã hết hạn. Vui lòng đăng nhập lại.");
        }
    }

    private static SkySoftReportRequest CreateReportPayload(
        DateTime fromDate,
        DateTime toDate,
        TimeSpan fromTime,
        TimeSpan toTime)
    {
        return new SkySoftReportRequest
        {
            ReportID = 32,
            PlateNo = string.Empty,
            FromDate = fromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ToDate = toDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            FromTime = fromTime.ToString(@"hh\:mm\:ss"),
            ToTime = toTime.ToString(@"hh\:mm\:ss"),
            UserID = 0,
            GroupID = 0,
            PlaceGroupID = 0,
            LineID = 0,
            JsonOutput = true,
            FilterByEndDate = false,
            VehicleIDs = [],
            GzipOutput = true
        };
    }

    private async Task<string> ExecuteReportWithRetryAsync(
        SkySoftReportRequest payload,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(_options.ReportRetryCount, 1, 5);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await ExecuteReportAsync(payload, cancellationToken);
            }
            catch (SkySoftGatewayTimeoutException) when (attempt < maxAttempts)
            {
                // 504 là do gateway phía SkySoft chủ động ngắt request.
                // Chờ theo cấu hình rồi thử lại cùng khoảng báo cáo.
                var delaySeconds = Math.Clamp(_options.ReportRetryDelaySeconds, 1, 120);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (SkySoftGatewayTimeoutException)
            {
                throw new InvalidOperationException(
                    $"SkySoft tạo báo cáo {fromDate:dd/MM/yyyy} → {toDate:dd/MM/yyyy} vẫn bị HTTP 504 sau {maxAttempts} lần thử. " +
                    "Dữ liệu local cũ được giữ nguyên; hãy thử Cập nhật lại sau.");
            }
        }

        throw new InvalidOperationException("Không thể tạo báo cáo SkySoft.");
    }

    private async Task<string> ExecuteReportAsync(
        SkySoftReportRequest payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "rest/report/v2/executeMobileReport")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Referrer = new Uri("https://go.skysoft.vn/");

        using var operationCts = CreateOperationTimeoutToken(
            cancellationToken,
            _options.ReportTimeoutMinutes);

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"SkySoft đóng gói báo cáo quá {_options.ReportTimeoutMinutes} phút nhưng chưa phản hồi.");
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(operationCts.Token);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SkySoftSessionExpiredException();

        if (response.StatusCode == HttpStatusCode.GatewayTimeout)
            throw new SkySoftGatewayTimeoutException();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"executeMobileReport lỗi HTTP {(int)response.StatusCode}: {Limit(json, 800)}");
        }

        if (LooksLikeHtml(response, json))
            throw new SkySoftSessionExpiredException();

        var result = JsonSerializer.Deserialize<SkySoftReportResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result is null)
            throw new InvalidOperationException("Không đọc được response executeMobileReport.");

        if (!string.Equals(result.ActionResult, "OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SkySoft actionResult = {result.ActionResult ?? "null"}.");

        if (string.IsNullOrWhiteSpace(result.FileID))
            throw new InvalidOperationException("SkySoft không trả fileID.");

            return result.FileID;
        }
    }

    private async Task<IReadOnlyList<ParsedTaxiTrip>> ReadExcelDetailAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        var endpoint = "rest/report/getExcelFile?fileID=" + Uri.EscapeDataString(fileId);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Referrer = new Uri("https://go.skysoft.vn/");

        using var operationCts = CreateOperationTimeoutToken(
            cancellationToken,
            _options.ExcelTimeoutMinutes);

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"SkySoft tải file Excel quá {_options.ExcelTimeoutMinutes} phút nhưng chưa hoàn tất.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new SkySoftSessionExpiredException();

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(operationCts.Token);
                throw new InvalidOperationException(
                    $"getExcelFile lỗi HTTP {(int)response.StatusCode}: {Limit(error, 800)}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                throw new SkySoftSessionExpiredException();

            // Không ghi .xls ra ổ đĩa. Toàn bộ file chỉ nằm trong RAM.
            await using var networkStream = await response.Content.ReadAsStreamAsync(operationCts.Token);
            await using var memory = new MemoryStream();
            await networkStream.CopyToAsync(memory, operationCts.Token);

            if (memory.Length == 0)
                throw new InvalidOperationException("SkySoft trả file Excel rỗng.");

            memory.Position = 0;
            return ReadDetailSheet(memory, "Chi tiết");
        }
    }

    private static IReadOnlyList<ParsedTaxiTrip> ReadDetailSheet(
        Stream excelStream,
        string sheetName)
    {
        using var reader = ExcelReaderFactory.CreateReader(excelStream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false,
                EmptyColumnNamePrefix = "Column"
            }
        });

        var source = dataSet.Tables.Cast<DataTable>().FirstOrDefault(t =>
            string.Equals(t.TableName.Trim(), sheetName, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            var available = string.Join(", ", dataSet.Tables.Cast<DataTable>().Select(t => t.TableName));
            throw new InvalidOperationException(
                $"Không tìm thấy sheet '{sheetName}'. Sheet hiện có: {available}");
        }

        var headerIndex = FindHeaderRow(source);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException(
                "Không tìm thấy dòng tiêu đề Chi tiết gồm: " + string.Join(" | ", ExpectedColumns));
        }

        var header = source.Rows[headerIndex];
        var columnMap = ExpectedColumns.ToDictionary(
            name => name,
            name => FindColumnIndex(header, name),
            StringComparer.OrdinalIgnoreCase);

        if (columnMap.Values.Any(index => index < 0))
            throw new InvalidOperationException("Cấu trúc cột sheet Chi tiết không đúng mẫu SkySoft.");

        var trips = new List<ParsedTaxiTrip>();

        for (var rowIndex = headerIndex + 1; rowIndex < source.Rows.Count; rowIndex++)
        {
            var row = source.Rows[rowIndex];
            if (IsEmptyRow(row))
                continue;

            var soHieu = CellText(row, columnMap["Số hiệu"]);
            var bienSo = CellText(row, columnMap["Biển số"]);
            var ketThucRaw = CellText(row, columnMap["Kết thúc"]);

            // Trong file mẫu SkySoft, sau mỗi xe có một dòng subtotal với Kết thúc = "Tổng tiền".
            if (string.Equals(ketThucRaw, "Tổng tiền", StringComparison.OrdinalIgnoreCase))
                continue;

            // Chỉ giữ đúng dòng cuốc thực tế.
            if (string.IsNullOrWhiteSpace(soHieu) || string.IsNullOrWhiteSpace(bienSo))
                continue;

            if (!TryExcelDate(row[columnMap["Bắt đầu"]], out var batDau) ||
                !TryExcelDate(row[columnMap["Kết thúc"]], out var ketThuc))
            {
                continue;
            }

            var kmCoKhach = ExcelDecimal(row[columnMap["KM có khách"]], 3);
            var kmRong = ExcelDecimal(row[columnMap["KM rỗng"]], 3);
            var tongKm = ExcelDecimal(row[columnMap["Tổng KM"]], 3);
            var thanhTien = ExcelDecimal(row[columnMap["Thành tiền"]], 0);
            var diemDau = CellText(row, columnMap["Điểm đầu"]);
            var diemCuoi = CellText(row, columnMap["Điểm cuối"]);

            trips.Add(new ParsedTaxiTrip(
                soHieu,
                bienSo,
                batDau,
                ketThuc,
                kmCoKhach,
                kmRong,
                tongKm,
                thanhTien,
                diemDau,
                diemCuoi));
        }

        // Cột A của file = Số hiệu. Sort theo cột A rồi thời gian bắt đầu.
        return trips
            .OrderBy(x => x.SoHieu, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.BatDau)
            .ToList();
    }

    private static int FindHeaderRow(DataTable table)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var names = row.ItemArray
                .Select(CellText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (ExpectedColumns.All(names.Contains))
                return rowIndex;
        }

        return -1;
    }

    private static int FindColumnIndex(DataRow header, string expectedName)
    {
        for (var i = 0; i < header.Table.Columns.Count; i++)
        {
            if (string.Equals(
                    CellText(header, i),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static CancellationTokenSource CreateOperationTimeoutToken(
        CancellationToken cancellationToken,
        int timeoutMinutes)
    {
        var minutes = Math.Clamp(timeoutMinutes, 1, 180);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(minutes));
        return cts;
    }

    private void CreateFreshSession()
    {
        _httpClient?.Dispose();
        _handler?.Dispose();

        _cookies = new CookieContainer();
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli
        };

        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri(string.IsNullOrWhiteSpace(_options.BaseUrl)
                ? "https://go.skysoft.vn/"
                : _options.BaseUrl),
            // Không dùng HttpClient.Timeout để tránh cấu hình cũ/biến môi trường
            // ép request về 60 giây. Mỗi operation SkySoft có timeout riêng.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://go.skysoft.vn");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/149.0.0.0 Safari/537.36");
    }

    private HttpClient Client => _httpClient
        ?? throw new ObjectDisposedException(nameof(SkySoftReportService));

    private bool HasTokenCookie()
    {
        var baseUri = Client.BaseAddress ?? new Uri("https://go.skysoft.vn/");
        var cookies = _cookies.GetCookies(baseUri);
        return cookies.Cast<Cookie>().Any(c =>
            c.Name.Equals("tokenID", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(c.Value));
    }

    private static bool IsEmptyRow(DataRow row) => row.ItemArray.All(IsEmpty);

    private static bool IsEmpty(object? value) =>
        value is null || value == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(value));

    private static string CellText(DataRow row, int index) =>
        index >= 0 && index < row.Table.Columns.Count ? CellText(row[index]) : string.Empty;

    private static string CellText(object? value)
    {
        if (value is null || value == DBNull.Value)
            return string.Empty;

        if (value is DateTime date)
            return date.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        return Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
    }

    private static bool TryExcelDate(object? value, out DateTime result)
    {
        if (value is DateTime date)
        {
            result = date;
            return true;
        }

        var text = CellText(value);
        var formats = new[]
        {
            "dd/MM/yyyy HH:mm:ss",
            "d/M/yyyy H:mm:ss",
            "dd/MM/yyyy H:mm:ss",
            "d/M/yyyy HH:mm:ss"
        };

        return DateTime.TryParseExact(
                   text,
                   formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out result)
               || DateTime.TryParse(text, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out result);
    }

    private static decimal ExcelDecimal(object? value, int decimals)
    {
        decimal number;

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        else
        {
            var text = CellText(value);
            if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out number) &&
                !decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("vi-VN"), out number))
            {
                return 0m;
            }
        }

        return decimal.Round(number, decimals, MidpointRounding.AwayFromZero);
    }

    private static bool LooksLikeHtml(HttpResponseMessage response, string body)
    {
        var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        return type.Contains("text/html", StringComparison.OrdinalIgnoreCase)
               || body.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase)
               || body.TrimStart().StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase);
    }

    private static string Limit(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max];
    }

    public void Dispose()
    {
        _loggedIn = false;
        _sessionLock.Dispose();
        _httpClient?.Dispose();
        _handler?.Dispose();
    }

    private sealed class SkySoftSessionExpiredException : Exception { }
    private sealed class SkySoftGatewayTimeoutException : Exception { }
}
