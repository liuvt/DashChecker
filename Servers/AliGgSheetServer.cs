using System.Globalization;
using System.Net;
using DashChecker.Extensions;
using DashChecker.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace DashChecker.Servers;

/// <summary>
/// Google Sheets server chuyên biệt cho module Ali (admin.nhaxeviet.vn).
/// Hỗ trợ xóa từ dòng 2 và ghi đè dữ liệu mới lên các sheet của Ali.
/// </summary>
public sealed class AliGgSheetServer
{
    private const string ApplicationName = "GoogleSheetWithToan";
    private const string DefaultSpreadsheetId = "1LrZoup8C2JnOmCqB9pzjlU5nKDTy_qgLUEYlKO8moME";

    public const string SheetAppKhachHang = "APP KHÁCH HÀNG";
    public const string SheetKhuyenMai = "KHUYẾN MÃI";
    public const string SheetCancel = "HỦY CUỐC TĐ/APP";
    public const string SheetSwitchboard = "KIỂM SOÁT DV HT";
    public const string SheetPartnerGsm = "ĐỐI TÁC GSM";
    public const string SheetPartnerVnPay = "ĐỐI TÁC VNPAY";
    public const string SheetOnlineApp = "ONLINE APP";

    private static readonly string[] Scopes = [SheetsService.Scope.Spreadsheets];
    private static readonly SemaphoreSlim SheetWriteLock = new(1, 1);

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AliGgSheetServer> _logger;
    private readonly Lazy<SheetsService> _sheetsService;

    public string SpreadSheetId { get; set; } = DefaultSpreadsheetId;

    public AliGgSheetServer(IWebHostEnvironment environment, ILogger<AliGgSheetServer> logger)
    {
        _environment = environment;
        _logger = logger;
        _sheetsService = new Lazy<SheetsService>(CreateService, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private SheetsService Service => _sheetsService.Value;

    private SheetsService CreateService()
    {
        var credentialPath = ResolveCredentialPath();
        using var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });
    }

    private string ResolveCredentialPath()
    {
        string[] candidates =
        [
            Path.Combine(_environment.ContentRootPath, "App_Data", "ali-ggsheetaccount.json"),
            Path.Combine(AppContext.BaseDirectory, "App_Data", "ali-ggsheetaccount.json"),
            Path.Combine(_environment.ContentRootPath, "App_Data", "ggsheetaccount.json"),
            Path.Combine(AppContext.BaseDirectory, "App_Data", "ggsheetaccount.json")
        ];

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            throw new FileNotFoundException(
                "Không tìm thấy file cấu hình Google Service Account cho Ali trong App_Data.");
        }

        return found;
    }

    public async Task<int> OverwriteOrderAliAsync(
        IReadOnlyList<AliOrder> orders,
        CancellationToken cancellationToken = default)
    {
        var values = orders.Select(item => (IList<object>)new List<object>
        {
            item.ID,
            item.CustomerPhoneNumber,
            item.CustomerFullName,
            item.Status,
            ParseDouble(item.Distance),
            item.DriverPhoneNumber,
            item.DriveNo,
            ParseDouble(item.Price),
            item.Location,
            item.BookingTime.ltvNormalizeDateTimeOrTime24h(),
            item.Note
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetAppKhachHang}!A2:K",
            $"{SheetAppKhachHang}!A2",
            values,
            "OverwriteOrderAli",
            cancellationToken);
        return values.Count;
    }

    public async Task<int> OverwritePromoteAliAsync(
        IReadOnlyList<AliPromote> promotes,
        CancellationToken cancellationToken = default)
    {
        var values = promotes.Select(item => (IList<object>)new List<object>
        {
            item.ID,
            item.PartnerCode,
            item.DriverPhoneNumber,
            ParseDouble(item.PriceTrip),
            ParseDouble(item.PromotionPrice),
            ParseDouble(item.VoucherPrice),
            ParseDouble(item.ReturnDiscount),
            ParseDouble(item.ExtraFee),
            ParseDouble(item.CustomerPay),
            item.PaymentMethod,
            item.CreatedAt.ltvNormalizeDateTimeOrTime24h()
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetKhuyenMai}!A2:K",
            $"{SheetKhuyenMai}!A2",
            values,
            "OverwritePromoteAli",
            cancellationToken);
        return values.Count;
    }

    public async Task<int> OverwriteCancelOrderAliAsync(
        IReadOnlyList<AliCancelOrder> cancels,
        CancellationToken cancellationToken = default)
    {
        var values = cancels.Select(item => (IList<object>)new List<object>
        {
            item.ID,
            item.CustomerPhoneNumber,
            item.CustomerFullName,
            item.Status,
            ParseDouble(item.Distance),
            item.DriverPhoneNumber,
            item.DriveNo,
            ParseDouble(item.Price),
            item.Location,
            item.BookingTime.ltvNormalizeDateTimeOrTime24h(),
            item.Note,
            item.Type
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetCancel}!A2:L",
            $"{SheetCancel}!A2",
            values,
            "OverwriteCancelOrderAli",
            cancellationToken);
        return values.Count;
    }

    public async Task<int> OverwriteSwitchboardAliAsync(
        IReadOnlyList<AliSwitchboard> switchboards,
        CancellationToken cancellationToken = default)
    {
        var values = switchboards.Select(item => (IList<object>)new List<object>
        {
            item.ID,
            item.CustomerPhoneNumber,
            item.CustomerFullName,
            item.Status,
            ParseDouble(item.Distance),
            item.DriverPhoneNumber,
            item.DriveNo,
            ParseDouble(item.Price),
            item.Location,
            item.BookingTime.ltvNormalizeDateTimeOrTime24h(),
            item.Note
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetSwitchboard}!A2:K",
            $"{SheetSwitchboard}!A2",
            values,
            "OverwriteSwitchboardAli",
            cancellationToken);
        return values.Count;
    }

    public async Task<int> OverwritePartnerGSMAliAsync(
        IReadOnlyList<AliPartnerGSM> partners,
        CancellationToken cancellationToken = default)
    {
        var values = partners.Select(item => (IList<object>)new List<object>
        {
            item.Id_partner,
            item.Id_ali,
            item.TripTime.ltvNormalizeDateTimeOrTime24h(),
            item.CustomerPhoneNumber,
            item.CustomerFullName,
            ParseDouble(item.Distance),
            ParseDouble(item.Price),
            item.PaymentMethod,
            item.PaymentStatus,
            item.DriverPhoneNumber,
            item.DriverId,
            item.DriveNo,
            item.TripStatus,
            item.PickupLocation,
            item.DropoffLocation,
            item.TypeService
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetPartnerGsm}!A2:P",
            $"{SheetPartnerGsm}!A2",
            values,
            "OverwritePartnerGSMAli",
            cancellationToken);
        return values.Count;
    }

    public async Task<int> OverwritePartnerVNPayAliAsync(
        IReadOnlyList<AliPartnerVNPay> partners,
        CancellationToken cancellationToken = default)
    {
        var values = partners.Select(item => (IList<object>)new List<object>
        {
            item.IdDoiTac,
            item.IdCongTy,
            item.IdHeThong,
            item.ThoiDiemPhatSinhCuocDi.ltvNormalizeDateTimeOrTime24h(),
            item.SdtKhachHang,
            item.TenKhachHang,
            ParseDouble(item.QuangDuong),
            ParseDouble(item.TienCuoc),
            item.HinhThucThanhToan,
            item.TrangThaiThanhToan,
            item.DienThoaiLaiXe,
            item.MaLaiXe,
            item.SoTai,
            item.BienSoXe,
            item.TrangThaiChuyenDi,
            item.DiemDon,
            item.DiemTra,
            item.DichVu
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetPartnerVnPay}!A2:R",
            $"{SheetPartnerVnPay}!A2",
            values,
            "OverwritePartnerVNPayAli",
            cancellationToken);
        return values.Count;
    }

    public async Task<int> OverwriteOnlineAliAsync(
        IReadOnlyList<AliOnlineApp> onlineRows,
        CancellationToken cancellationToken = default)
    {
        var values = onlineRows.Select(item => (IList<object>)new List<object>
        {
            item.ID,
            item.DriverName,
            item.DriverPhoneNumber,
            item.DriveNo,
            item.DrivePlate,
            item.Team,
            item.OnlineTime.ltvNormalizeDateTimeOrTime24h()
        }).ToList();

        await ClearAndWriteAsync(
            $"{SheetOnlineApp}!A2:G",
            $"{SheetOnlineApp}!A2",
            values,
            "OverwriteOnlineAli",
            cancellationToken);
        return values.Count;
    }

    private async Task ClearAndWriteAsync(
        string clearRange,
        string writeStartCell,
        IList<IList<object>> values,
        string operationName,
        CancellationToken cancellationToken)
    {
        await SheetWriteLock.WaitAsync(cancellationToken);
        try
        {
            await ExecuteWithRetryAsync(
                $"{operationName}.Clear({clearRange})",
                async () =>
                {
                    var clearReq = Service.Spreadsheets.Values.Clear(new ClearValuesRequest(), SpreadSheetId, clearRange);
                    await clearReq.ExecuteAsync(cancellationToken);
                },
                cancellationToken);

            if (values is null || values.Count == 0)
                return;

            await ExecuteWithRetryAsync(
                $"{operationName}.Update({writeStartCell}, rows={values.Count})",
                async () =>
                {
                    var body = new ValueRange { Values = values };
                    var updateReq = Service.Spreadsheets.Values.Update(body, SpreadSheetId, writeStartCell);
                    updateReq.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                    await updateReq.ExecuteAsync(cancellationToken);
                },
                cancellationToken);
        }
        finally
        {
            SheetWriteLock.Release();
        }
    }

    private async Task ExecuteWithRetryAsync(
        string operationName,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 4;
        var delayMs = 1000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (GoogleApiException gex) when (attempt < maxRetries && IsTransientGoogleError(gex.HttpStatusCode))
            {
                _logger.LogWarning(
                    "Google Sheets tạm thời bận ({StatusCode}) tại {Operation}, thử lại {Attempt}/{Max}...",
                    (int)gex.HttpStatusCode,
                    operationName,
                    attempt,
                    maxRetries);
            }
            catch (HttpRequestException hex) when (attempt < maxRetries)
            {
                _logger.LogWarning(
                    "Lỗi mạng khi gọi Google Sheets ({Operation}): {Message}, thử lại {Attempt}/{Max}...",
                    operationName,
                    hex.Message,
                    attempt,
                    maxRetries);
            }

            await Task.Delay(delayMs + Random.Shared.Next(0, 300), cancellationToken);
            delayMs = Math.Min(delayMs * 2, 10000);
        }
    }

    private static bool IsTransientGoogleError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static double ParseDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var normalized = raw.Trim().Replace(" ", "");
        if (normalized.Contains(',') && !normalized.Contains('.'))
            normalized = normalized.Replace(',', '.');
        else
            normalized = normalized.Replace(",", "");

        return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0;
    }
}
