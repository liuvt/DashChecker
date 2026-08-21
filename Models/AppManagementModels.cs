using System.Text.Json;
using DashChecker.Entities;

namespace DashChecker.Models;

public sealed record AppManagedColumn(string Key, string Label, string InputType = "text", string CssClass = "");

public sealed record AppModuleDefinition(string Key, string Title, string ShortTitle, IReadOnlyList<AppManagedColumn> Columns)
{
    public static readonly IReadOnlyDictionary<string, AppModuleDefinition> All =
        new Dictionary<string, AppModuleDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["vnpay"] = new(
                "vnpay", "App VNPay", "VNPay",
                [
                    new("PartnerId", "ID ĐỐI TÁC"),
                    new("CompanyId", "ID CÔNG TY"),
                    new("SystemId", "ID HỆ THỐNG"),
                    new("TripOccurredAt", "Thời điểm phát sinh cuốc đi", "datetime-local", "datetime-col"),
                    new("CustomerPhone", "SĐT khách hàng"),
                    new("CustomerName", "Tên khách hàng"),
                    new("Distance", "Quãng đường", "number", "number-col"),
                    new("Fare", "Tiền cước", "number", "money-col"),
                    new("PaymentMethod", "Hình thức Thanh toán"),
                    new("PaymentStatus", "Trạng thái thanh toán"),
                    new("DriverPhone", "Điện thoại Lái xe"),
                    new("DriverCode", "Mã Lái xe"),
                    new("VehicleNo", "Số tài"),
                    new("PlateNo", "Biển số xe"),
                    new("TripStatus", "Trạng thái chuyến đi"),
                    new("Pickup", "Điểm đón", "text", "wide-text-col"),
                    new("Dropoff", "Điểm trả", "text", "wide-text-col"),
                    new("Service", "Dịch vụ"),
                    new("DataStatus", "Trạng Thái")
                ]),

            ["customer-promo"] = new(
                "customer-promo", "Khuyến mãi App Khách Hàng", "Khuyến mãi App KH",
                [
                    new("TripId", "ID cuốc xe"),
                    new("PartnerCode", "Mã đối tác"),
                    new("DriverPhone", "SĐT lái xe"),
                    new("Fare", "Tiền cước cuốc xe", "number", "money-col"),
                    new("Promotion", "Tiền khuyến mãi", "number", "money-col"),
                    new("ReturnDiscount", "Giảm giá chiều về", "number", "money-col"),
                    new("CustomerPay", "Khách hàng phải trả", "number", "money-col"),
                    new("Surcharge", "Phụ phí", "number", "money-col"),
                    new("Discount", "Chiết khấu", "number", "money-col"),
                    new("Revenue", "Doanh thu", "number", "money-col"),
                    new("RemainingDeposit", "Tiền ký quỹ còn lại", "number", "money-col"),
                    new("PaymentMethod", "Phương thức thanh toán")
                ]),

            ["customer-app"] = new(
                "customer-app", "App Khách Hàng", "App Khách Hàng",
                [
                    new("Id", "ID"),
                    new("CustomerPhone", "Điện thoại KH"),
                    new("CustomerName", "Họ tên"),
                    new("TripStatus", "Trạng thái"),
                    new("Km", "KM", "number", "number-col"),
                    new("DriverPhone", "Điện thoại tài xế"),
                    new("VehicleNo", "Số tài"),
                    new("Fare", "Tiền cước", "number", "money-col"),
                    new("Location", "Địa điểm", "text", "wide-text-col"),
                    new("BookingTime", "Thời điểm đặt xe / lên xuống xe", "datetime-local", "datetime-col"),
                    new("Note", "Ghi chú", "text", "wide-text-col"),
                    new("DataStatus", "Trạng Thái dữ liệu")
                ]),

            ["xanh-sm"] = new(
                "xanh-sm", "App Xanh SM", "Xanh SM",
                [
                    new("Id", "ID"),
                    new("CustomerPhone", "Customer Phone Number"),
                    new("Depot", "Depot"),
                    new("Status", "Status"),
                    new("Distance", "Distance", "number", "number-col"),
                    new("DriverPhone", "Driver Phone Number"),
                    new("VehicleNo", "Số Tài"),
                    new("TotalFeeDisplay", "Total Fee Display", "number", "money-col"),
                    new("TotalPayDisplay", "Total Pay Display", "number", "money-col"),
                    new("TipAmount", "Tip Amount", "number", "money-col"),
                    new("BookingTime", "Thời điểm đặt xe / lên xuống xe", "datetime-local", "datetime-col"),
                    new("PaymentMethod", "Payment Method"),
                    new("Note", "Ghi chú", "text", "wide-text-col"),
                    new("DataStatus", "Trạng Thái dữ liệu")
                ]),

            ["contracts"] = new(
                "contracts", "Hợp đồng", "Hợp đồng",
                [
                    new("Id", "ID"),
                    new("Date", "Ngày", "date", "date-col"),
                    new("Time", "Giờ", "time", "time-col"),
                    new("VehicleNo", "Số Tài"),
                    new("Pickup", "Đón", "text", "wide-text-col"),
                    new("Dropoff", "Trả", "text", "wide-text-col"),
                    new("Km", "Số km", "number", "number-col"),
                    new("WaitHours", "Giờ chờ", "number", "number-col"),
                    new("Price", "Giá", "number", "money-col"),
                    new("AgreedPrice", "Giá thỏa thuận", "number", "money-col"),
                    new("SC", "SC"),
                    new("Surcharge", "Phụ Thu", "number", "money-col"),
                    new("DriverCollect", "Lái xe thu", "number", "money-col"),
                    new("Dispatcher", "Người phát điểm"),
                    new("ContractType", "Loại hợp đồng"),
                    new("Status", "Trạng Thái"),
                    new("Note", "Ghi chú", "text", "wide-text-col")
                ])
        };

    public static AppModuleDefinition Get(string? key) =>
        key is not null && All.TryGetValue(key, out var module) ? module : All["vnpay"];
}

public sealed record AppManagedRowView(
    long Id,
    int RowOrder,
    IReadOnlyDictionary<string, string> Values,
    DateTime CreatedAt,
    DateTime? SavedAt);

public sealed record AppManagedSnapshot(
    IReadOnlyList<AppManagedRowView> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    string Search,
    bool IsArchive)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)Math.Max(1, PageSize)));
}

public static class AppManagedJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Dictionary<string, string> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
            return new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string Serialize(IReadOnlyDictionary<string, string> values) =>
        JsonSerializer.Serialize(values, JsonOptions);
}
