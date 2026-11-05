using System.Data;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Extensions;
using DashChecker.Models;
using DashChecker.Servers;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;

namespace DashChecker.Services;

/// <summary>
/// Dịch vụ tích hợp Ali (admin.nhaxeviet.vn) vào DashChecker:
/// - Đăng nhập & lưu phiên (AliAuthentication.json)
/// - Tải & parse báo cáo Excel 9 tỉnh (App Khách hàng, Khuyến mãi, Cuốc hủy, Tổng đài HT, Đối tác GSM, Đối tác VNPay, Online App)
/// - Ghi dữ liệu lên Google Sheet (AliGgSheetServer)
/// </summary>
public sealed class AliService
{
    private const string AuthFileName = "AliAuthentication.json";
    private const string EndpointLogin = "account/login";
    private const string EndpointOrder = "request/widget?";
    private const string EndpointPromote = "transaction/money?";
    private const string EndpointSwitchboard = "request?";
    private const string EndpointPartnerGSM = "widget-request/widget-request-gsm?";
    private const string EndpointPartnerVNPay = "widget-request/widget-request-vnpay-tch?";
    private const string EndpointOnlineApp = "statistic/driver-onlinev2?";

    private static readonly Uri BaseUri = new("https://admin.nhaxeviet.vn/");

    public static readonly IReadOnlyList<AliProvinceOption> Provinces =
    [
        new("64", "BẠC LIÊU", "BL"),
        new("61", "VĨNH LONG", "VL"),
        new("63", "CÀ MAU", "CM"),
        new("15", "KIÊN GIANG", "RG"),
        new("62", "HẬU GIANG", "HG"),
        new("16", "AN GIANG", "AG"),
        new("20", "SÓC TRĂNG", "CT"),
        new("5", "CẦN THƠ", "CT"),
        new("11", "PHÚ QUỐC", "PQ")
    ];

    private static readonly CookieContainer CookieContainer = new();
    private static readonly HttpClient SharedClient = CreateSharedHttpClient();
    private static AliSchemaJson? _cachedSchema;
    private static DateTime _lastAppliedCookieUtc = DateTime.MinValue;

    private static readonly SemaphoreSlim ExportGate = new(1, 1);
    private static DateTime _lastExportCompletedUtc = DateTime.MinValue;
    private const int RequestMinDelayMs = 600;
    private const int ExcelMaxAttempts = 4;
    private static readonly TimeSpan ExcelRequestTimeout = TimeSpan.FromMinutes(3);

    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly AliGgSheetServer _sheetServer;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AliService> _logger;

    public event Action<string>? ProgressChanged;
    public event Action? AuthChanged;
    public event Action? LocalDataChanged;

    public bool IsConnected => _cachedSchema?.CookieAli is { Count: > 0 };
    public string ConnectedUserName => _cachedSchema?.User?.username ?? string.Empty;
    public int CookieCount => _cachedSchema?.CookieAli?.Count ?? 0;
    public string FirstCookieName => _cachedSchema?.CookieAli?.FirstOrDefault()?.key ?? "N/A";
    public DateTime? LastConnectedAt { get; private set; }
    public string StatusMessage { get; private set; } = "Chưa kết nối";

    public AliService(
        IDbContextFactory<LocalDbContext> dbFactory,
        AliGgSheetServer sheetServer,
        IWebHostEnvironment environment,
        ILogger<AliService> logger)
    {
        _dbFactory = dbFactory;
        _sheetServer = sheetServer;
        _environment = environment;
        _logger = logger;
        _ = LoadAuthenticationAsync();
    }

    private static HttpClient CreateSharedHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            CookieContainer = CookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            MaxConnectionsPerServer = 6,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = BaseUri,
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    private void ReportProgress(string message) => ProgressChanged?.Invoke(message);

    private string ResolveAuthFilePath()
    {
        var appDataFile = Path.Combine(_environment.ContentRootPath, "App_Data", AuthFileName);
        if (File.Exists(appDataFile))
            return appDataFile;

        var rootFile = Path.Combine(_environment.ContentRootPath, AuthFileName);
        if (File.Exists(rootFile))
            return rootFile;

        var baseDirFile = Path.Combine(AppContext.BaseDirectory, AuthFileName);
        if (File.Exists(baseDirFile))
            return baseDirFile;

        return appDataFile;
    }

    public async Task<AliSchemaJson?> LoadAuthenticationAsync()
    {
        if (_cachedSchema != null)
            return _cachedSchema;

        var path = ResolveAuthFilePath();
        if (!File.Exists(path))
            return null;

        var jsonText = await File.ReadAllTextAsync(path);
        _cachedSchema = JsonSerializer.Deserialize<AliSchemaJson>(jsonText);
        if (_cachedSchema?.CookieAli is { Count: > 0 })
        {
            ApplyCookies(_cachedSchema);
            LastConnectedAt ??= VietnamClock.FromUtc(File.GetLastWriteTimeUtc(path));
            StatusMessage = $"Đã kết nối ({_cachedSchema.User?.username ?? "Ali"} · {_cachedSchema.CookieAli.Count} cookie)";
        }
        else if (_cachedSchema?.User is not null && !string.IsNullOrWhiteSpace(_cachedSchema.User.username))
        {
            StatusMessage = $"Đã lưu tài khoản ({_cachedSchema.User.username}) · Chưa kết nối phiên";
        }

        AuthChanged?.Invoke();
        return _cachedSchema;
    }

    private void SaveAuthenticationToFile(AliSchemaJson schemaJson)
    {
        _cachedSchema = schemaJson;
        var option = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(schemaJson, option);
        var utf8NoBom = new UTF8Encoding(false);

        var primaryPath = Path.Combine(_environment.ContentRootPath, "App_Data", AuthFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(primaryPath)!);
        File.WriteAllText(primaryPath, json, utf8NoBom);
    }

    private static void ApplyCookies(AliSchemaJson? json)
    {
        var target = _cachedSchema ?? json;
        if (target?.CookieAli == null || target.CookieAli.Count == 0)
            throw new InvalidOperationException("AliAuthentication không có cookie hợp lệ. Vui lòng kết nối tài khoản Ali tại mục Quản lý Liên kết.");

        foreach (var ck in target.CookieAli)
        {
            if (!string.IsNullOrWhiteSpace(ck.key))
                CookieContainer.Add(BaseUri, new Cookie(ck.key, ck.value ?? string.Empty));
        }

        _lastAppliedCookieUtc = DateTime.UtcNow;
    }

    private async Task EnsureCookiesAppliedAsync(AliSchemaJson? providedSchema = null)
    {
        if (providedSchema != null && _cachedSchema == null)
            _cachedSchema = providedSchema;

        if (_cachedSchema == null)
            await LoadAuthenticationAsync();

        var schema = _cachedSchema ?? providedSchema;
        if (schema?.CookieAli is not { Count: > 0 })
        {
            if (await TryReauthenticateFromSavedUserAsync())
                schema = _cachedSchema;
        }

        if (schema == null || schema.CookieAli is not { Count: > 0 })
            throw new InvalidOperationException("Tài khoản Ali (admin.nhaxeviet.vn) chưa kết nối. Vui lòng vào mục Quản lý Liên kết để Connect tài khoản Ali.");

        if (CookieContainer.GetCookies(BaseUri).Count == 0 ||
            (DateTime.UtcNow - _lastAppliedCookieUtc) > TimeSpan.FromMinutes(5))
        {
            ApplyCookies(schema);
        }
    }

    public async Task<bool> AuthenticateAsync(AliUser user)
    {
        if (user == null || string.IsNullOrWhiteSpace(user.username) || string.IsNullOrWhiteSpace(user.password))
            throw new ArgumentException("Vui lòng nhập đầy đủ tài khoản và mật khẩu Ali.");

        var cleanUser = new AliUser
        {
            username = user.username.Trim(),
            password = user.password
        };

        var postData = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(nameof(cleanUser.username), cleanUser.username),
            new KeyValuePair<string, string>(nameof(cleanUser.password), cleanUser.password)
        ]);

        using var response = await SharedClient.PostAsync(EndpointLogin, postData);
        var body = await response.Content.ReadAsStringAsync();

        var finalPath = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
        var isStillOnLoginPage =
            finalPath.Contains("account/login", StringComparison.OrdinalIgnoreCase) &&
            (body.Contains("ĐĂNG NHẬP THẤT BẠI", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("Login Page - Taxi Admin", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("Please Enter Your Information", StringComparison.OrdinalIgnoreCase));

        if (!response.IsSuccessStatusCode || isStillOnLoginPage)
            return false;

        var cookies = CookieContainer.GetCookies(BaseUri);
        var cookieList = cookies.Cast<Cookie>()
            .Select(c => new AliCookie
            {
                key = c.Name,
                value = c.Value
            })
            .ToList();

        if (cookieList.Count == 0)
            return false;

        var schemaJson = new AliSchemaJson
        {
            User = cleanUser,
            CookieAli = cookieList
        };

        SaveAuthenticationToFile(schemaJson);
        _lastAppliedCookieUtc = DateTime.UtcNow;
        LastConnectedAt = VietnamClock.Now;
        StatusMessage = $"Đã kết nối: {cleanUser.username} ({cookieList.Count} cookie) lúc {LastConnectedAt:HH:mm:ss}";
        AuthChanged?.Invoke();
        return true;
    }

    public void Disconnect()
    {
        foreach (Cookie ck in CookieContainer.GetCookies(BaseUri))
        {
            ck.Expired = true;
        }

        if (_cachedSchema != null)
        {
            _cachedSchema.CookieAli = [];
            SaveAuthenticationToFile(_cachedSchema);
        }

        StatusMessage = $"Đã ngắt kết nối phiên Ali lúc {VietnamClock.Now:HH:mm:ss}";
        AuthChanged?.Invoke();
    }

    private async Task<bool> TryReauthenticateFromSavedUserAsync()
    {
        var user = _cachedSchema?.User;
        if (user == null || string.IsNullOrWhiteSpace(user.username) || string.IsNullOrWhiteSpace(user.password))
        {
            var loaded = await LoadAuthenticationAsync();
            user = loaded?.User;
        }

        if (user == null || string.IsNullOrWhiteSpace(user.username) || string.IsNullOrWhiteSpace(user.password))
            return false;

        try
        {
            ReportProgress("Phiên Ali hết hạn, đang tự động đăng nhập lại...");
            return await AuthenticateAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto re-authentication failed on {BaseUri}", BaseUri);
            return false;
        }
    }

    private static IReadOnlyList<AliProvinceOption> ResolveProvinces(string? provinceFilter)
    {
        if (string.IsNullOrWhiteSpace(provinceFilter) ||
            string.Equals(provinceFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return Provinces;
        }

        var matched = Provinces
            .Where(x => string.Equals(x.Code, provinceFilter, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.AreaCodeHint, provinceFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matched.Count > 0 ? matched : Provinces;
    }

    // ========================= 1. APP KHÁCH HÀNG (OrderAli) =========================

    public async Task<List<AliOrder>> GetOrdersAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        var targets = ResolveProvinces(provinceFilter);
        var allOrders = new List<AliOrder>();

        for (var i = 0; i < targets.Count; i++)
        {
            var p = targets[i];
            ReportProgress($"Đang tải Đơn hàng App KH: {p.Name} ({i + 1}/{targets.Count})...");
            var rows = await FetchOrdersByProvinceAsync(p.Code, p.Name, start, end, cancellationToken);
            allOrders.AddRange(rows);
        }

        await SaveLocalOrdersAsync(allOrders, targets.Select(x => x.Name).ToList(), cancellationToken);
        return allOrders;
    }

    public async Task<List<AliOrder>> SyncOrdersToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        var orders = await GetOrdersAsync(start, end, provinceFilter, cancellationToken);
        ReportProgress($"Đang ghi {orders.Count:N0} đơn hàng App KH lên Google Sheet...");
        await _sheetServer.OverwriteOrderAliAsync(orders, cancellationToken);
        return orders;
    }

    private async Task<List<AliOrder>> FetchOrdersByProvinceAsync(
        string areaCode,
        string provinceName,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken)
    {
        var url = BaseUri + EndpointOrder + BuildOrderQuery(areaCode, start, end);
        return await ReadAliExcelWithRetryAsync(
            url,
            $"Order area={areaCode}",
            table =>
            {
                var orders = new List<AliOrder>();
                foreach (var row in GetNonEmptyRows(table).Skip(1))
                {
                    var id = Cell(row, 2);
                    if (string.IsNullOrWhiteSpace(id) && IsRowAllBlank(row))
                        continue;

                    orders.Add(new AliOrder
                    {
                        ID = id,
                        CustomerPhoneNumber = Cell(row, 3),
                        CustomerFullName = Cell(row, 4),
                        Status = Cell(row, 5),
                        Distance = Cell(row, 6).Replace(".", ","),
                        DriverPhoneNumber = Cell(row, 7),
                        DriveNo = Cell(row, 8),
                        Price = Cell(row, 9),
                        Location = Cell(row, 10),
                        BookingTime = CellTime24h(row, 11),
                        Note = Cell(row, 12),
                        ProvinceName = provinceName
                    });
                }
                return orders;
            },
            cancellationToken);
    }

    private static string BuildOrderQuery(string area, DateTime startTime, DateTime endTime)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["province"] = area;
        query["phone_client"] = "";
        query["phone_driver"] = "";
        query["request_id"] = "";
        query["content"] = "";
        query["taxi_code"] = "";
        query["distance"] = "0";
        query["agency_id"] = "0";
        query["time_finish_request"] = "0";
        query["agency_handle"] = "-1";
        query["widget_id"] = "48";
        query["request_status[]"] = "3";
        query["taxi_type"] = "0";
        query["team_id"] = "0";
        query["from-date-request"] = startTime.ToString("dd-MM-yyyy HH:mm");
        query["to-date-request"] = endTime.ToString("dd-MM-yyyy HH:mm");
        query["driver_online"] = "0";
        query["status_statistic"] = "1";
        query["export_excel"] = "1";
        return query.ToString()!;
    }

    // ========================= 2. KHUYẾN MÃI (PromoteAli) =========================

    public async Task<List<AliPromote>> GetPromotesAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        var targets = ResolveProvinces(provinceFilter);
        var allPromotes = new List<AliPromote>();

        for (var i = 0; i < targets.Count; i++)
        {
            var p = targets[i];
            ReportProgress($"Đang tải Khuyến mãi: {p.Name} ({i + 1}/{targets.Count})...");
            var url = BaseUri + EndpointPromote + BuildPromoteQuery(p.Code, start, end);
            var rows = await ReadAliExcelWithRetryAsync(
                url,
                $"Promote area={p.Code}",
                table =>
                {
                    var list = new List<AliPromote>();
                    foreach (var row in GetNonEmptyRows(table).Skip(1))
                    {
                        var id = Cell(row, 2);
                        if (string.IsNullOrWhiteSpace(id) && IsRowAllBlank(row))
                            continue;

                        list.Add(new AliPromote
                        {
                            ID = id,
                            PartnerCode = Cell(row, 3),
                            DriverPhoneNumber = Cell(row, 4),
                            PriceTrip = Cell(row, 5),
                            PromotionPrice = Cell(row, 6),
                            VoucherPrice = Cell(row, 7),
                            ReturnDiscount = Cell(row, 8),
                            ExtraFee = Cell(row, 9),
                            CustomerPay = Cell(row, 10),
                            PaymentMethod = Cell(row, 11),
                            CreatedAt = CellTime24h(row, 12),
                            ProvinceName = p.Name
                        });
                    }
                    return list;
                },
                cancellationToken);
            allPromotes.AddRange(rows);
        }

        await SaveLocalPromotesAsync(allPromotes, targets.Select(x => x.Name).ToList(), cancellationToken);
        return allPromotes;
    }

    public async Task<List<AliPromote>> SyncPromotesToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        var promotes = await GetPromotesAsync(start, end, provinceFilter, cancellationToken);
        ReportProgress($"Đang ghi {promotes.Count:N0} khuyến mãi lên Google Sheet...");
        await _sheetServer.OverwritePromoteAliAsync(promotes, cancellationToken);
        return promotes;
    }

    private static string BuildPromoteQuery(string area, DateTime startTime, DateTime endTime)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["province"] = area;
        query["driver_code"] = "";
        query["phone"] = "";
        query["request_id"] = "";
        query["payment_method_id"] = "0";
        query["payment_status"] = "1";
        query["team_id"] = "0";
        query["from-date"] = startTime.ToString("dd-MM-yyyy HH:mm");
        query["to-date"] = endTime.ToString("dd-MM-yyyy HH:mm");
        query["promote_status"] = "1";
        query["act"] = "2";
        return query.ToString()!;
    }

    // ========================= 3. CUỐC HỦY (CancelOrder) =========================

    public async Task<List<AliCancelOrder>> GetCancelOrdersAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        var targets = ResolveProvinces(provinceFilter);
        var switchboardCancels = new List<AliCancelOrder>();
        var appCancels = new List<AliCancelOrder>();

        for (var i = 0; i < targets.Count; i++)
        {
            var p = targets[i];
            ReportProgress($"Đang tải Hủy Tổng đài: {p.Name} ({i + 1}/{targets.Count})...");
            var url = BaseUri + EndpointSwitchboard + BuildCancelQuery(p.Code, start, end);
            var rows = await ParseCancelSheetAsync(url, $"CancelSwitchboard area={p.Code}", "TỔNG ĐÀI", p.Name, cancellationToken);
            switchboardCancels.AddRange(rows);
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var p = targets[i];
            ReportProgress($"Đang tải Hủy App KH: {p.Name} ({i + 1}/{targets.Count})...");
            var url = BaseUri + EndpointOrder + BuildCancelQuery(p.Code, start, end);
            var rows = await ParseCancelSheetAsync(url, $"CancelOrder area={p.Code}", "APP KHÁCH HÀNG", p.Name, cancellationToken);
            appCancels.AddRange(rows);
        }

        var combined = appCancels.Concat(switchboardCancels).ToList();
        await SaveLocalCancelOrdersAsync(combined, targets.Select(x => x.Name).ToList(), cancellationToken);
        return combined;
    }

    public async Task<List<AliCancelOrder>> SyncCancelOrdersToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        var cancels = await GetCancelOrdersAsync(start, end, provinceFilter, cancellationToken);
        ReportProgress($"Đang ghi {cancels.Count:N0} cuốc hủy lên Google Sheet...");
        await _sheetServer.OverwriteCancelOrderAliAsync(cancels, cancellationToken);
        return cancels;
    }

    private async Task<List<AliCancelOrder>> ParseCancelSheetAsync(
        string url,
        string operation,
        string sourceType,
        string provinceName,
        CancellationToken cancellationToken)
    {
        return await ReadAliExcelWithRetryAsync(
            url,
            operation,
            table =>
            {
                var list = new List<AliCancelOrder>();
                foreach (var row in GetNonEmptyRows(table).Skip(1))
                {
                    var driveNo = Cell(row, 8);
                    if (string.IsNullOrWhiteSpace(driveNo))
                        continue;

                    list.Add(new AliCancelOrder
                    {
                        ID = Cell(row, 2),
                        CustomerPhoneNumber = Cell(row, 3),
                        CustomerFullName = Cell(row, 4),
                        Status = Cell(row, 5),
                        Distance = Cell(row, 6).Replace(".", ","),
                        DriverPhoneNumber = Cell(row, 7),
                        DriveNo = driveNo.ToUpperInvariant(),
                        Price = Cell(row, 9),
                        Location = Cell(row, 10),
                        BookingTime = CellTime24h(row, 11),
                        Note = Cell(row, 12),
                        Type = sourceType,
                        ProvinceName = provinceName
                    });
                }
                return list;
            },
            cancellationToken);
    }

    private static string BuildCancelQuery(string area, DateTime startTime, DateTime endTime)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["province"] = area;
        query["phone_client"] = "";
        query["phone_driver"] = "";
        query["request_id"] = "";
        query["content"] = "";
        query["taxi_code"] = "";
        query["distance"] = "0";
        query["agency_id"] = "0";
        query["time_finish_request"] = "0";
        query["agency_handle"] = "-1";
        query["taxi_type"] = "0";
        query["team_id"] = "0";
        query["from-date-request"] = startTime.ToString("dd-MM-yyyy HH:mm");
        query["to-date-request"] = endTime.ToString("dd-MM-yyyy HH:mm");
        query["driver_online"] = "0";
        query["status_statistic"] = "1";
        query["export_excel"] = "1";

        var list = query.AllKeys
            .Where(k => k != null)
            .Select(k => $"{HttpUtility.UrlEncode(k)}={HttpUtility.UrlEncode(query[k])}")
            .ToList();

        foreach (var status in new[] { "4", "6", "8" })
            list.Add($"request_status[]={HttpUtility.UrlEncode(status)}");

        return string.Join("&", list);
    }

    // ========================= 4. TỔNG ĐÀI HOÀN THÀNH (SwitchboardAli) =========================

    public async Task<List<AliSwitchboard>> GetSwitchboardsAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        var targets = ResolveProvinces(provinceFilter);
        var switchBoards = new List<AliSwitchboard>();

        for (var i = 0; i < targets.Count; i++)
        {
            var p = targets[i];
            ReportProgress($"Đang tải Cuốc Tổng đài: {p.Name} ({i + 1}/{targets.Count})...");
            var url = BaseUri + EndpointSwitchboard + BuildSwitchboardQuery(p.Code, start, end);
            var rows = await ReadAliExcelWithRetryAsync(
                url,
                $"Switchboard area={p.Code}",
                table =>
                {
                    var list = new List<AliSwitchboard>();
                    foreach (var row in GetNonEmptyRows(table).Skip(1))
                    {
                        var driveNo = Cell(row, 8);
                        if (string.IsNullOrWhiteSpace(driveNo))
                            continue;

                        list.Add(new AliSwitchboard
                        {
                            ID = Cell(row, 2),
                            CustomerPhoneNumber = Cell(row, 3),
                            CustomerFullName = Cell(row, 4),
                            Status = Cell(row, 5),
                            Distance = Cell(row, 6).Replace(".", ","),
                            DriverPhoneNumber = Cell(row, 7),
                            DriveNo = driveNo.ToUpperInvariant(),
                            Price = Cell(row, 9),
                            Location = Cell(row, 10),
                            BookingTime = CellTime24h(row, 11),
                            Note = Cell(row, 12),
                            ProvinceName = p.Name
                        });
                    }
                    return list;
                },
                cancellationToken);
            switchBoards.AddRange(rows);
        }

        await SaveLocalSwitchboardsAsync(switchBoards, targets.Select(x => x.Name).ToList(), cancellationToken);
        return switchBoards;
    }

    public async Task<List<AliSwitchboard>> SyncSwitchboardsToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        var switchboards = await GetSwitchboardsAsync(start, end, provinceFilter, cancellationToken);
        ReportProgress($"Đang ghi {switchboards.Count:N0} cuốc tổng đài lên Google Sheet...");
        await _sheetServer.OverwriteSwitchboardAliAsync(switchboards, cancellationToken);
        return switchboards;
    }

    private static string BuildSwitchboardQuery(string area, DateTime startTime, DateTime endTime)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["province"] = area;
        query["phone_client"] = "";
        query["phone_driver"] = "";
        query["request_id"] = "";
        query["content"] = "";
        query["taxi_code"] = "";
        query["distance"] = "0";
        query["agency_id"] = "0";
        query["time_finish_request"] = "0";
        query["agency_handle"] = "-1";
        query["request_status[]"] = "3";
        query["taxi_type"] = "0";
        query["team_id"] = "0";
        query["from-date-request"] = startTime.ToString("dd-MM-yyyy HH:mm");
        query["to-date-request"] = endTime.ToString("dd-MM-yyyy HH:mm");
        query["driver_online"] = "0";
        query["status_statistic"] = "1";
        query["export_excel"] = "1";
        return query.ToString()!;
    }

    // ========================= 5. ĐỐI TÁC GSM (PartnerGSM) =========================

    public async Task<List<AliPartnerGSM>> GetPartnerGsmAsync(
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        ReportProgress("Đang tải dữ liệu Đối tác GSM (Tất cả khu vực)...");

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["partner_order_id"] = "";
        query["request_id"] = "";
        query["phone_client"] = "";
        query["phone_driver"] = "";
        query["province_id"] = "0";
        query["payment_method_id"] = "0";
        query["from-date-request"] = start.ToString("dd-MM-yyyy HH:mm");
        query["to-date-request"] = end.ToString("dd-MM-yyyy HH:mm");
        query["status"] = "3";
        query["payment_status"] = "-1";
        query["export_excel"] = "1";

        var url = BaseUri + EndpointPartnerGSM + query;
        var gsmList = await ReadAliExcelWithRetryAsync(
            url,
            "Partner GSM",
            table =>
            {
                var partners = new List<AliPartnerGSM>();
                foreach (var row in GetNonEmptyRows(table).Skip(2))
                {
                    var idPartner = Cell(row, 2);
                    if (string.IsNullOrWhiteSpace(idPartner) && IsRowAllBlank(row))
                        continue;

                    partners.Add(new AliPartnerGSM
                    {
                        Id_partner = idPartner,
                        Id_ali = Cell(row, 3),
                        TripTime = CellTime24h(row, 4),
                        CustomerPhoneNumber = Cell(row, 5),
                        CustomerFullName = Cell(row, 6),
                        Distance = Cell(row, 7).Replace(".", ","),
                        Price = Cell(row, 8).Replace(",", ""),
                        PaymentMethod = Cell(row, 9),
                        PaymentStatus = Cell(row, 10),
                        DriverPhoneNumber = Cell(row, 11),
                        DriverId = Cell(row, 12),
                        DriveNo = Cell(row, 13),
                        TripStatus = Cell(row, 14),
                        PickupLocation = Cell(row, 15),
                        DropoffLocation = Cell(row, 16),
                        TypeService = Cell(row, 17)
                    });
                }
                return partners;
            },
            cancellationToken);

        await SaveLocalPartnerGsmAsync(gsmList, cancellationToken);
        return gsmList;
    }

    public async Task<List<AliPartnerGSM>> SyncPartnerGsmToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        var partners = await GetPartnerGsmAsync(start, end, cancellationToken);
        ReportProgress($"Đang ghi {partners.Count:N0} cuốc Đối tác GSM lên Google Sheet...");
        await _sheetServer.OverwritePartnerGSMAliAsync(partners, cancellationToken);
        return partners;
    }

    // ========================= 6. ĐỐI TÁC VNPAY (PartnerVNPay) =========================

    public async Task<List<AliPartnerVNPay>> GetPartnerVnPayAsync(
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        ReportProgress("Đang tải dữ liệu Đối tác VNPay (Tất cả khu vực)...");

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["partner_order_id"] = "";
        query["widget_request_id"] = "";
        query["request_id"] = "";
        query["phone_client"] = "";
        query["province_id"] = "0";
        query["payment_status"] = "1";
        query["from-date-request"] = start.ToString("dd-MM-yyyy HH:mm");
        query["to-date-request"] = end.ToString("dd-MM-yyyy HH:mm");
        query["status"] = "-3";
        query["taxi_code"] = "";
        query["content"] = "";
        query["payment_method_id"] = "0";
        query["phone_driver"] = "";
        query["request_type_id"] = "0";
        query["export_excel"] = "1";

        var url = BaseUri + EndpointPartnerVNPay + query;
        var vnpayList = await ReadAliExcelWithRetryAsync(
            url,
            "Partner VNPay",
            table =>
            {
                var partners = new List<AliPartnerVNPay>();
                foreach (var row in GetNonEmptyRows(table).Skip(2))
                {
                    var idDoiTac = Cell(row, 2);
                    if (string.IsNullOrWhiteSpace(idDoiTac) && IsRowAllBlank(row))
                        continue;

                    partners.Add(new AliPartnerVNPay
                    {
                        IdDoiTac = idDoiTac,
                        IdCongTy = Cell(row, 3),
                        IdHeThong = Cell(row, 4),
                        ThoiDiemPhatSinhCuocDi = CellTime24h(row, 5),
                        SdtKhachHang = Cell(row, 6),
                        TenKhachHang = Cell(row, 7),
                        QuangDuong = Cell(row, 8).Replace(".", ","),
                        TienCuoc = Cell(row, 9).Replace(",", "."),
                        HinhThucThanhToan = Cell(row, 10),
                        TrangThaiThanhToan = Cell(row, 11),
                        DienThoaiLaiXe = Cell(row, 12),
                        MaLaiXe = Cell(row, 13),
                        SoTai = Cell(row, 14),
                        BienSoXe = Cell(row, 15),
                        TrangThaiChuyenDi = Cell(row, 16),
                        DiemDon = Cell(row, 17),
                        DiemTra = Cell(row, 18),
                        DichVu = Cell(row, 19)
                    });
                }
                return partners;
            },
            cancellationToken);

        await SaveLocalPartnerVnPayAsync(vnpayList, cancellationToken);
        return vnpayList;
    }

    public async Task<List<AliPartnerVNPay>> SyncPartnerVnPayToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        var partners = await GetPartnerVnPayAsync(start, end, cancellationToken);
        ReportProgress($"Đang ghi {partners.Count:N0} cuốc Đối tác VNPay lên Google Sheet...");
        await _sheetServer.OverwritePartnerVNPayAliAsync(partners, cancellationToken);
        return partners;
    }

    // ========================= 7. ONLINE APP (OnlineAppAli) =========================

    public async Task<List<AliOnlineApp>> GetOnlineAppsAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        await EnsureCookiesAppliedAsync();
        var targets = ResolveProvinces(provinceFilter);
        var allData = new List<AliOnlineApp>();

        for (var i = 0; i < targets.Count; i++)
        {
            var p = targets[i];
            ReportProgress($"Đang tải Online App: {p.Name} ({i + 1}/{targets.Count})...");

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["province"] = p.Code;
            query["driver_code"] = "";
            query["taxi_code"] = "";
            query["taxi_serial"] = "";
            query["phone_driver"] = "";
            query["from-date"] = start.ToString("dd-MM-yyyy HH:mm");
            query["to-date"] = end.ToString("dd-MM-yyyy HH:mm");
            query["status_statistic"] = "1";
            query["act"] = "2";

            var url = $"{BaseUri}{EndpointOnlineApp}{query}";
            var rows = await ReadAliExcelWithRetryAsync(
                url,
                $"OnlineApp area={p.Code}",
                table =>
                {
                    var datas = new List<AliOnlineApp>();
                    foreach (var row in GetNonEmptyRows(table).Skip(3))
                    {
                        if (IsRowAllBlank(row))
                            continue;

                        datas.Add(new AliOnlineApp
                        {
                            ID = Cell(row, 2),
                            DriverName = Cell(row, 3),
                            DriverPhoneNumber = Cell(row, 4),
                            DriveNo = Cell(row, 5),
                            DrivePlate = Cell(row, 6),
                            Team = Cell(row, 7),
                            OnlineTime = CellTime24h(row, 8),
                            ProvinceName = p.Name
                        });
                    }
                    return datas;
                },
                cancellationToken);
            allData.AddRange(rows);
        }

        await SaveLocalOnlineAppsAsync(allData, targets.Select(x => x.Name).ToList(), cancellationToken);
        return allData;
    }

    public async Task<List<AliOnlineApp>> SyncOnlineAppsToGoogleSheetAsync(
        DateTime start,
        DateTime end,
        string? provinceFilter = "ALL",
        CancellationToken cancellationToken = default)
    {
        var data = await GetOnlineAppsAsync(start, end, provinceFilter, cancellationToken);
        ReportProgress($"Đang ghi {data.Count:N0} bản ghi Online App lên Google Sheet...");
        await _sheetServer.OverwriteOnlineAliAsync(data, cancellationToken);
        return data;
    }

    // ========================= LƯU & ĐỌC CỤC BỘ TRONG SQLITE =========================
    // Quy tắc bắt buộc khi đẩy vào Database Local:
    // 1. Kiểm tra ngày tạo (CreatedAt trong khoảng dayStart -> dayEnd của ngày hiện hành) và mã bản ghi nguồn (ID).
    // 2. Nếu chạy 2 lần (hoặc nhiều lần) trong cùng ngày tạo / cùng dữ liệu nguồn thì XÓA/CẬP NHẬT lại bản ghi cũ,
    //    tuyệt đối KHÔNG lưu chồng (duplicate) dữ liệu vào bảng.

    private static string ExtractDatePrefix(string? rawDateTime)
    {
        if (string.IsNullOrWhiteSpace(rawDateTime)) return string.Empty;
        var trimmed = rawDateTime.Trim();
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx].Trim() : trimmed;
    }

    public async Task SaveLocalOrdersAsync(
        IReadOnlyList<AliOrder> rows,
        IReadOnlyCollection<string>? targetProvinceNames = null,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        // Lọc trùng ngay trên tập dữ liệu nguồn (nếu chạy trùng hoặc API trả lặp ID)
        var deduped = rows
            .GroupBy(
                x => $"{(x.ID ?? string.Empty).Trim()}|{(x.ProvinceName ?? string.Empty).Trim()}|{(x.BookingTime ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingIds = deduped
            .Select(x => (x.ID ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var provinceSet = targetProvinceNames is { Count: > 0 } && targetProvinceNames.Count < Provinces.Count
            ? targetProvinceNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Kiểm tra ngày tạo (CreatedAt) và mã đơn nguồn: nếu chạy lần 2 thì xóa dữ liệu cũ của ngày đó / mã nguồn đó trước khi ghi mới
        if (provinceSet is null)
        {
            await db.AliOrders
                .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                            (incomingIds.Count > 0 && incomingIds.Contains(x.OrderId)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.AliOrders
                .Where(x => provinceSet.Contains(x.ProvinceName) &&
                            ((x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                             (incomingIds.Count > 0 && incomingIds.Contains(x.OrderId))))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliOrderRecord
            {
                RowOrder = i + 1,
                OrderId = (x.ID ?? string.Empty).Trim(),
                CustomerPhoneNumber = x.CustomerPhoneNumber ?? string.Empty,
                CustomerFullName = x.CustomerFullName ?? string.Empty,
                Status = x.Status ?? string.Empty,
                Distance = x.Distance ?? string.Empty,
                DriverPhoneNumber = x.DriverPhoneNumber ?? string.Empty,
                DriveNo = x.DriveNo ?? string.Empty,
                Price = x.Price ?? string.Empty,
                Location = x.Location ?? string.Empty,
                BookingTime = x.BookingTime ?? string.Empty,
                Note = x.Note ?? string.Empty,
                ProvinceName = x.ProvinceName ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliOrders.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliOrder>> GetLocalOrdersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliOrders.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliOrders.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.OrderId}|{x.ProvinceName}|{x.BookingTime}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliOrder
            {
                ID = x.OrderId,
                CustomerPhoneNumber = x.CustomerPhoneNumber,
                CustomerFullName = x.CustomerFullName,
                Status = x.Status,
                Distance = x.Distance,
                DriverPhoneNumber = x.DriverPhoneNumber,
                DriveNo = x.DriveNo,
                Price = x.Price,
                Location = x.Location,
                BookingTime = x.BookingTime,
                Note = x.Note,
                ProvinceName = x.ProvinceName
            }).ToList();
    }

    public async Task SaveLocalPromotesAsync(
        IReadOnlyList<AliPromote> rows,
        IReadOnlyCollection<string>? targetProvinceNames = null,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.ID ?? string.Empty).Trim()}|{(x.PartnerCode ?? string.Empty).Trim()}|{(x.ProvinceName ?? string.Empty).Trim()}|{(x.CreatedAt ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingIds = deduped
            .Select(x => (x.ID ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var provinceSet = targetProvinceNames is { Count: > 0 } && targetProvinceNames.Count < Provinces.Count
            ? targetProvinceNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (provinceSet is null)
        {
            await db.AliPromotes
                .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                            (incomingIds.Count > 0 && incomingIds.Contains(x.PromoteId)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.AliPromotes
                .Where(x => provinceSet.Contains(x.ProvinceName) &&
                            ((x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                             (incomingIds.Count > 0 && incomingIds.Contains(x.PromoteId))))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliPromoteRecord
            {
                RowOrder = i + 1,
                PromoteId = (x.ID ?? string.Empty).Trim(),
                PartnerCode = x.PartnerCode ?? string.Empty,
                DriverPhoneNumber = x.DriverPhoneNumber ?? string.Empty,
                PriceTrip = x.PriceTrip ?? string.Empty,
                PromotionPrice = x.PromotionPrice ?? string.Empty,
                VoucherPrice = x.VoucherPrice ?? string.Empty,
                ReturnDiscount = x.ReturnDiscount ?? string.Empty,
                ExtraFee = x.ExtraFee ?? string.Empty,
                CustomerPay = x.CustomerPay ?? string.Empty,
                PaymentMethod = x.PaymentMethod ?? string.Empty,
                SourceCreatedAt = x.CreatedAt ?? string.Empty,
                ProvinceName = x.ProvinceName ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliPromotes.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliPromote>> GetLocalPromotesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliPromotes.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliPromotes.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.PromoteId}|{x.PartnerCode}|{x.ProvinceName}|{x.SourceCreatedAt}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliPromote
            {
                ID = x.PromoteId,
                PartnerCode = x.PartnerCode,
                DriverPhoneNumber = x.DriverPhoneNumber,
                PriceTrip = x.PriceTrip,
                PromotionPrice = x.PromotionPrice,
                VoucherPrice = x.VoucherPrice,
                ReturnDiscount = x.ReturnDiscount,
                ExtraFee = x.ExtraFee,
                CustomerPay = x.CustomerPay,
                PaymentMethod = x.PaymentMethod,
                CreatedAt = x.SourceCreatedAt,
                ProvinceName = x.ProvinceName
            }).ToList();
    }

    public async Task SaveLocalCancelOrdersAsync(
        IReadOnlyList<AliCancelOrder> rows,
        IReadOnlyCollection<string>? targetProvinceNames = null,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.ID ?? string.Empty).Trim()}|{(x.Type ?? string.Empty).Trim()}|{(x.DriveNo ?? string.Empty).Trim()}|{(x.ProvinceName ?? string.Empty).Trim()}|{(x.BookingTime ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingIds = deduped
            .Select(x => (x.ID ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var provinceSet = targetProvinceNames is { Count: > 0 } && targetProvinceNames.Count < Provinces.Count
            ? targetProvinceNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (provinceSet is null)
        {
            await db.AliCancelOrders
                .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                            (incomingIds.Count > 0 && incomingIds.Contains(x.OrderId)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.AliCancelOrders
                .Where(x => provinceSet.Contains(x.ProvinceName) &&
                            ((x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                             (incomingIds.Count > 0 && incomingIds.Contains(x.OrderId))))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliCancelOrderRecord
            {
                RowOrder = i + 1,
                OrderId = (x.ID ?? string.Empty).Trim(),
                CustomerPhoneNumber = x.CustomerPhoneNumber ?? string.Empty,
                CustomerFullName = x.CustomerFullName ?? string.Empty,
                Status = x.Status ?? string.Empty,
                Distance = x.Distance ?? string.Empty,
                DriverPhoneNumber = x.DriverPhoneNumber ?? string.Empty,
                DriveNo = x.DriveNo ?? string.Empty,
                Price = x.Price ?? string.Empty,
                Location = x.Location ?? string.Empty,
                BookingTime = x.BookingTime ?? string.Empty,
                Note = x.Note ?? string.Empty,
                Type = x.Type ?? string.Empty,
                ProvinceName = x.ProvinceName ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliCancelOrders.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliCancelOrder>> GetLocalCancelOrdersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliCancelOrders.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliCancelOrders.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.OrderId}|{x.Type}|{x.DriveNo}|{x.ProvinceName}|{x.BookingTime}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliCancelOrder
            {
                ID = x.OrderId,
                CustomerPhoneNumber = x.CustomerPhoneNumber,
                CustomerFullName = x.CustomerFullName,
                Status = x.Status,
                Distance = x.Distance,
                DriverPhoneNumber = x.DriverPhoneNumber,
                DriveNo = x.DriveNo,
                Price = x.Price,
                Location = x.Location,
                BookingTime = x.BookingTime,
                Note = x.Note,
                Type = x.Type,
                ProvinceName = x.ProvinceName
            }).ToList();
    }

    public async Task SaveLocalSwitchboardsAsync(
        IReadOnlyList<AliSwitchboard> rows,
        IReadOnlyCollection<string>? targetProvinceNames = null,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.ID ?? string.Empty).Trim()}|{(x.DriveNo ?? string.Empty).Trim()}|{(x.ProvinceName ?? string.Empty).Trim()}|{(x.BookingTime ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingIds = deduped
            .Select(x => (x.ID ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var provinceSet = targetProvinceNames is { Count: > 0 } && targetProvinceNames.Count < Provinces.Count
            ? targetProvinceNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (provinceSet is null)
        {
            await db.AliSwitchboards
                .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                            (incomingIds.Count > 0 && incomingIds.Contains(x.OrderId)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.AliSwitchboards
                .Where(x => provinceSet.Contains(x.ProvinceName) &&
                            ((x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                             (incomingIds.Count > 0 && incomingIds.Contains(x.OrderId))))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliSwitchboardRecord
            {
                RowOrder = i + 1,
                OrderId = (x.ID ?? string.Empty).Trim(),
                CustomerPhoneNumber = x.CustomerPhoneNumber ?? string.Empty,
                CustomerFullName = x.CustomerFullName ?? string.Empty,
                Status = x.Status ?? string.Empty,
                Distance = x.Distance ?? string.Empty,
                DriverPhoneNumber = x.DriverPhoneNumber ?? string.Empty,
                DriveNo = x.DriveNo ?? string.Empty,
                Price = x.Price ?? string.Empty,
                Location = x.Location ?? string.Empty,
                BookingTime = x.BookingTime ?? string.Empty,
                Note = x.Note ?? string.Empty,
                ProvinceName = x.ProvinceName ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliSwitchboards.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliSwitchboard>> GetLocalSwitchboardsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliSwitchboards.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliSwitchboards.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.OrderId}|{x.DriveNo}|{x.ProvinceName}|{x.BookingTime}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliSwitchboard
            {
                ID = x.OrderId,
                CustomerPhoneNumber = x.CustomerPhoneNumber,
                CustomerFullName = x.CustomerFullName,
                Status = x.Status,
                Distance = x.Distance,
                DriverPhoneNumber = x.DriverPhoneNumber,
                DriveNo = x.DriveNo,
                Price = x.Price,
                Location = x.Location,
                BookingTime = x.BookingTime,
                Note = x.Note,
                ProvinceName = x.ProvinceName
            }).ToList();
    }

    public async Task SaveLocalPartnerGsmAsync(IReadOnlyList<AliPartnerGSM> rows, CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.Id_partner ?? string.Empty).Trim()}|{(x.Id_ali ?? string.Empty).Trim()}|{(x.TripTime ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingPartnerIds = deduped
            .Select(x => (x.Id_partner ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.AliPartnerGsms
            .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                        (incomingPartnerIds.Count > 0 && incomingPartnerIds.Contains(x.IdPartner)))
            .ExecuteDeleteAsync(cancellationToken);

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliPartnerGsmRecord
            {
                RowOrder = i + 1,
                IdPartner = (x.Id_partner ?? string.Empty).Trim(),
                IdAli = (x.Id_ali ?? string.Empty).Trim(),
                TripTime = x.TripTime ?? string.Empty,
                CustomerPhoneNumber = x.CustomerPhoneNumber ?? string.Empty,
                CustomerFullName = x.CustomerFullName ?? string.Empty,
                Distance = x.Distance ?? string.Empty,
                Price = x.Price ?? string.Empty,
                PaymentMethod = x.PaymentMethod ?? string.Empty,
                PaymentStatus = x.PaymentStatus ?? string.Empty,
                DriverPhoneNumber = x.DriverPhoneNumber ?? string.Empty,
                DriverId = x.DriverId ?? string.Empty,
                DriveNo = x.DriveNo ?? string.Empty,
                TripStatus = x.TripStatus ?? string.Empty,
                PickupLocation = x.PickupLocation ?? string.Empty,
                DropoffLocation = x.DropoffLocation ?? string.Empty,
                TypeService = x.TypeService ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliPartnerGsms.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliPartnerGSM>> GetLocalPartnerGsmAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliPartnerGsms.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliPartnerGsms.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.IdPartner}|{x.IdAli}|{x.TripTime}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliPartnerGSM
            {
                Id_partner = x.IdPartner,
                Id_ali = x.IdAli,
                TripTime = x.TripTime,
                CustomerPhoneNumber = x.CustomerPhoneNumber,
                CustomerFullName = x.CustomerFullName,
                Distance = x.Distance,
                Price = x.Price,
                PaymentMethod = x.PaymentMethod,
                PaymentStatus = x.PaymentStatus,
                DriverPhoneNumber = x.DriverPhoneNumber,
                DriverId = x.DriverId,
                DriveNo = x.DriveNo,
                TripStatus = x.TripStatus,
                PickupLocation = x.PickupLocation,
                DropoffLocation = x.DropoffLocation,
                TypeService = x.TypeService
            }).ToList();
    }

    public async Task SaveLocalPartnerVnPayAsync(IReadOnlyList<AliPartnerVNPay> rows, CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.IdDoiTac ?? string.Empty).Trim()}|{(x.IdHeThong ?? string.Empty).Trim()}|{(x.ThoiDiemPhatSinhCuocDi ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingDoiTacIds = deduped
            .Select(x => (x.IdDoiTac ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.AliPartnerVnPays
            .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                        (incomingDoiTacIds.Count > 0 && incomingDoiTacIds.Contains(x.IdDoiTac)))
            .ExecuteDeleteAsync(cancellationToken);

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliPartnerVnPayRecord
            {
                RowOrder = i + 1,
                IdDoiTac = (x.IdDoiTac ?? string.Empty).Trim(),
                IdCongTy = x.IdCongTy ?? string.Empty,
                IdHeThong = (x.IdHeThong ?? string.Empty).Trim(),
                ThoiDiemPhatSinhCuocDi = x.ThoiDiemPhatSinhCuocDi ?? string.Empty,
                SdtKhachHang = x.SdtKhachHang ?? string.Empty,
                TenKhachHang = x.TenKhachHang ?? string.Empty,
                QuangDuong = x.QuangDuong ?? string.Empty,
                TienCuoc = x.TienCuoc ?? string.Empty,
                HinhThucThanhToan = x.HinhThucThanhToan ?? string.Empty,
                TrangThaiThanhToan = x.TrangThaiThanhToan ?? string.Empty,
                DienThoaiLaiXe = x.DienThoaiLaiXe ?? string.Empty,
                MaLaiXe = x.MaLaiXe ?? string.Empty,
                SoTai = x.SoTai ?? string.Empty,
                BienSoXe = x.BienSoXe ?? string.Empty,
                TrangThaiChuyenDi = x.TrangThaiChuyenDi ?? string.Empty,
                DiemDon = x.DiemDon ?? string.Empty,
                DiemTra = x.DiemTra ?? string.Empty,
                DichVu = x.DichVu ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliPartnerVnPays.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliPartnerVNPay>> GetLocalPartnerVnPayAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliPartnerVnPays.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliPartnerVnPays.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.IdDoiTac}|{x.IdHeThong}|{x.ThoiDiemPhatSinhCuocDi}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliPartnerVNPay
            {
                IdDoiTac = x.IdDoiTac,
                IdCongTy = x.IdCongTy,
                IdHeThong = x.IdHeThong,
                ThoiDiemPhatSinhCuocDi = x.ThoiDiemPhatSinhCuocDi,
                SdtKhachHang = x.SdtKhachHang,
                TenKhachHang = x.TenKhachHang,
                QuangDuong = x.QuangDuong,
                TienCuoc = x.TienCuoc,
                HinhThucThanhToan = x.HinhThucThanhToan,
                TrangThaiThanhToan = x.TrangThaiThanhToan,
                DienThoaiLaiXe = x.DienThoaiLaiXe,
                MaLaiXe = x.MaLaiXe,
                SoTai = x.SoTai,
                BienSoXe = x.BienSoXe,
                TrangThaiChuyenDi = x.TrangThaiChuyenDi,
                DiemDon = x.DiemDon,
                DiemTra = x.DiemTra,
                DichVu = x.DichVu
            }).ToList();
    }

    public async Task SaveLocalOnlineAppsAsync(
        IReadOnlyList<AliOnlineApp> rows,
        IReadOnlyCollection<string>? targetProvinceNames = null,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.ID ?? string.Empty).Trim()}|{(x.DriveNo ?? string.Empty).Trim()}|{(x.DrivePlate ?? string.Empty).Trim()}|{(x.ProvinceName ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var incomingDriverIds = deduped
            .Select(x => (x.ID ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var provinceSet = targetProvinceNames is { Count: > 0 } && targetProvinceNames.Count < Provinces.Count
            ? targetProvinceNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (provinceSet is null)
        {
            await db.AliOnlineApps
                .Where(x => (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                            (incomingDriverIds.Count > 0 && incomingDriverIds.Contains(x.DriverRecordId)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.AliOnlineApps
                .Where(x => provinceSet.Contains(x.ProvinceName) &&
                            ((x.CreatedAt >= dayStart && x.CreatedAt < dayEnd) ||
                             (incomingDriverIds.Count > 0 && incomingDriverIds.Contains(x.DriverRecordId))))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (deduped.Count > 0)
        {
            var entities = deduped.Select((x, i) => new AliOnlineAppRecord
            {
                RowOrder = i + 1,
                DriverRecordId = (x.ID ?? string.Empty).Trim(),
                DriverName = x.DriverName ?? string.Empty,
                DriverPhoneNumber = x.DriverPhoneNumber ?? string.Empty,
                DriveNo = x.DriveNo ?? string.Empty,
                DrivePlate = x.DrivePlate ?? string.Empty,
                Team = x.Team ?? string.Empty,
                OnlineTime = x.OnlineTime ?? string.Empty,
                ProvinceName = x.ProvinceName ?? string.Empty,
                CreatedAt = now
            }).ToList();

            db.AliOnlineApps.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        LocalDataChanged?.Invoke();
    }

    public async Task<List<AliOnlineApp>> GetLocalOnlineAppsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var latestCreated = await db.AliOnlineApps.AsNoTracking()
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        var query = db.AliOnlineApps.AsNoTracking();
        if (latestCreated.HasValue)
        {
            var dayStart = latestCreated.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(x => x.CreatedAt >= dayStart && x.CreatedAt < dayEnd);
        }

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => $"{x.DriverRecordId}|{x.DriveNo}|{x.DrivePlate}|{x.ProvinceName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new AliOnlineApp
            {
                ID = x.DriverRecordId,
                DriverName = x.DriverName,
                DriverPhoneNumber = x.DriverPhoneNumber,
                DriveNo = x.DriveNo,
                DrivePlate = x.DrivePlate,
                Team = x.Team,
                OnlineTime = x.OnlineTime,
                ProvinceName = x.ProvinceName
            }).ToList();
    }

    // ========================= EXCEL READER ENGINE =========================

    private async Task<T> ReadAliExcelWithRetryAsync<T>(
        string url,
        string operation,
        Func<DataTable, T> parser,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        var retryDelayMs = 1000;
        var reauthAttempted = false;

        for (var attempt = 1; attempt <= ExcelMaxAttempts; attempt++)
        {
            try
            {
                var bytes = await DownloadAliExcelOnceAsync(url, operation, cancellationToken);
                using var ms = new MemoryStream(bytes, writable: false);
                using var reader = ExcelReaderFactory.CreateReader(ms);
                var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration
                    {
                        UseHeaderRow = false
                    }
                });

                var table = dataSet.Tables.Count > 0 ? dataSet.Tables[0] : new DataTable();
                return parser(table);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AliSessionExpiredException sessionEx)
            {
                lastError = sessionEx;
                if (!reauthAttempted)
                {
                    reauthAttempted = true;
                    if (await TryReauthenticateFromSavedUserAsync())
                        continue;
                }

                throw new InvalidOperationException(
                    "Phiên đăng nhập trên admin.nhaxeviet.vn đã hết hạn hoặc thông tin tài khoản chưa đúng. Vui lòng đăng nhập lại!",
                    sessionEx);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Ali Excel {Operation} failed attempt {Attempt}/{MaxAttempts}", operation, attempt, ExcelMaxAttempts);
                if (attempt >= ExcelMaxAttempts)
                    break;

                await Task.Delay(retryDelayMs + Random.Shared.Next(0, 300), cancellationToken);
                retryDelayMs = Math.Min(retryDelayMs * 2, 8000);
            }
        }

        throw new InvalidOperationException(
            $"Không thể export/đọc Excel Ali sau {ExcelMaxAttempts} lần ({operation}): {lastError?.Message}",
            lastError);
    }

    private static async Task<byte[]> DownloadAliExcelOnceAsync(
        string url,
        string operation,
        CancellationToken cancellationToken)
    {
        await ExportGate.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTime.UtcNow - _lastExportCompletedUtc;
            var minDelay = TimeSpan.FromMilliseconds(RequestMinDelayMs);
            if (elapsed < minDelay)
                await Task.Delay(minDelay - elapsed, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ExcelRequestTimeout);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await SharedClient.SendAsync(
                req,
                HttpCompletionOption.ResponseContentRead,
                timeoutCts.Token);

            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AliSessionExpiredException($"HTTP {(int)res.StatusCode} khi export {operation}");

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)res.StatusCode} {res.ReasonPhrase} khi export {operation}");

            var finalPath = res.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
            if (finalPath.Contains("account/login", StringComparison.OrdinalIgnoreCase))
                throw new AliSessionExpiredException($"Server chuyển hướng về trang đăng nhập ({operation}).");

            var mediaType = res.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var bytes = await res.Content.ReadAsByteArrayAsync(timeoutCts.Token);

            if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                throw new AliSessionExpiredException($"Server trả về HTML thay vì Excel ({operation}); phiên đăng nhập đã hết hạn.");

            if (bytes.Length < 4 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
                throw new InvalidDataException($"Nội dung không phải XLSX hợp lệ ({operation}), bytes={bytes.Length}.");

            return bytes;
        }
        finally
        {
            _lastExportCompletedUtc = DateTime.UtcNow;
            ExportGate.Release();
        }
    }

    private static List<DataRow> GetNonEmptyRows(DataTable table)
    {
        var result = new List<DataRow>();
        foreach (DataRow row in table.Rows)
        {
            if (!IsRowAllBlank(row))
                result.Add(row);
        }
        return result;
    }

    private static bool IsRowAllBlank(DataRow row)
    {
        foreach (var item in row.ItemArray)
        {
            if (item is not null and not DBNull && !string.IsNullOrWhiteSpace(item.ToString()))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Lấy giá trị cột theo chỉ số 1-based (tương đương row.Cell(oneBasedColumn) trong ClosedXML).
    /// </summary>
    private static string Cell(DataRow row, int oneBasedColumn)
    {
        var index = oneBasedColumn - 1;
        if (index < 0 || index >= row.Table.Columns.Count)
            return string.Empty;

        var raw = row[index];
        if (raw is null or DBNull)
            return string.Empty;

        if (raw is DateTime dt)
        {
            if (dt.Year <= 1900)
                return dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            return dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : dt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (raw is TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:00}:{Math.Abs(ts.Minutes):00}:{Math.Abs(ts.Seconds):00}";
        }

        return Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static string CellTime24h(DataRow row, int oneBasedColumn) =>
        Cell(row, oneBasedColumn).ltvNormalizeDateTimeOrTime24h();

    private sealed class AliSessionExpiredException(string message) : Exception(message);
}

