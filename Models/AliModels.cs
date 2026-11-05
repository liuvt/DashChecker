using System.ComponentModel.DataAnnotations;

namespace DashChecker.Models;

public sealed class AliSchemaJson
{
    public AliUser User { get; set; } = new();
    public List<AliCookie> CookieAli { get; set; } = [];
}

public sealed class AliUser
{
    [Required(ErrorMessage = "Tài khoản không được để trống")]
    public string username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    public string password { get; set; } = string.Empty;
}

public sealed class AliCookie
{
    public string key { get; set; } = string.Empty;
    public string value { get; set; } = string.Empty;
}

public sealed class AliOrder
{
    public string ID { get; set; } = string.Empty;
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public string CustomerFullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Distance { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string DriveNo { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string BookingTime { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
}

public sealed class AliPromote
{
    public string ID { get; set; } = string.Empty;
    public string PartnerCode { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string PriceTrip { get; set; } = string.Empty;
    public string PromotionPrice { get; set; } = string.Empty;
    public string VoucherPrice { get; set; } = string.Empty;
    public string ReturnDiscount { get; set; } = string.Empty;
    public string ExtraFee { get; set; } = string.Empty;
    public string CustomerPay { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
}

public sealed class AliCancelOrder
{
    public string ID { get; set; } = string.Empty;
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public string CustomerFullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Distance { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string DriveNo { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string BookingTime { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
}

public sealed class AliSwitchboard
{
    public string ID { get; set; } = string.Empty;
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public string CustomerFullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Distance { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string DriveNo { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string BookingTime { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
}

public sealed class AliPartnerGSM
{
    public string Id_partner { get; set; } = string.Empty;
    public string Id_ali { get; set; } = string.Empty;
    public string TripTime { get; set; } = string.Empty;
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public string CustomerFullName { get; set; } = string.Empty;
    public string Distance { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public string DriveNo { get; set; } = string.Empty;
    public string TripStatus { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public string DropoffLocation { get; set; } = string.Empty;
    public string TypeService { get; set; } = string.Empty;
}

public sealed class AliPartnerVNPay
{
    public string IdDoiTac { get; set; } = string.Empty;
    public string IdCongTy { get; set; } = string.Empty;
    public string IdHeThong { get; set; } = string.Empty;
    public string ThoiDiemPhatSinhCuocDi { get; set; } = string.Empty;
    public string SdtKhachHang { get; set; } = string.Empty;
    public string TenKhachHang { get; set; } = string.Empty;
    public string QuangDuong { get; set; } = string.Empty;
    public string TienCuoc { get; set; } = string.Empty;
    public string HinhThucThanhToan { get; set; } = string.Empty;
    public string TrangThaiThanhToan { get; set; } = string.Empty;
    public string DienThoaiLaiXe { get; set; } = string.Empty;
    public string MaLaiXe { get; set; } = string.Empty;
    public string SoTai { get; set; } = string.Empty;
    public string BienSoXe { get; set; } = string.Empty;
    public string TrangThaiChuyenDi { get; set; } = string.Empty;
    public string DiemDon { get; set; } = string.Empty;
    public string DiemTra { get; set; } = string.Empty;
    public string DichVu { get; set; } = string.Empty;
}

public sealed class AliOnlineApp
{
    public string ID { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string DriveNo { get; set; } = string.Empty;
    public string DrivePlate { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string OnlineTime { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
}

public sealed record AliProvinceOption(string Code, string Name, string AreaCodeHint);

public sealed record AliScheduleJobInfo(
    string Key,
    string Title,
    string SheetName,
    TimeSpan DefaultTime,
    string Description);
