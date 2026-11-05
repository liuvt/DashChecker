namespace DashChecker.Entities;

/// <summary>
/// Bảng lưu cục bộ đơn hàng App Khách hàng (Ali - admin.nhaxeviet.vn) trong SQLite.
/// </summary>
public sealed class AliOrderRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
    public string OrderId { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Bảng lưu cục bộ Khuyến mãi (Ali) trong SQLite.
/// </summary>
public sealed class AliPromoteRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
    public string PromoteId { get; set; } = string.Empty;
    public string PartnerCode { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string PriceTrip { get; set; } = string.Empty;
    public string PromotionPrice { get; set; } = string.Empty;
    public string VoucherPrice { get; set; } = string.Empty;
    public string ReturnDiscount { get; set; } = string.Empty;
    public string ExtraFee { get; set; } = string.Empty;
    public string CustomerPay { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string SourceCreatedAt { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Bảng lưu cục bộ Cuốc hủy Tổng đài / App KH (Ali) trong SQLite.
/// </summary>
public sealed class AliCancelOrderRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
    public string OrderId { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Bảng lưu cục bộ Cuốc Tổng đài hoàn thành (Ali) trong SQLite.
/// </summary>
public sealed class AliSwitchboardRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
    public string OrderId { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Bảng lưu cục bộ Đối tác GSM (Ali) trong SQLite.
/// </summary>
public sealed class AliPartnerGsmRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
    public string IdPartner { get; set; } = string.Empty;
    public string IdAli { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Bảng lưu cục bộ Đối tác VNPay (Ali) trong SQLite.
/// </summary>
public sealed class AliPartnerVnPayRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
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
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Bảng lưu cục bộ Thống kê Online App (Ali) trong SQLite.
/// </summary>
public sealed class AliOnlineAppRecord
{
    public long Id { get; set; }
    public int RowOrder { get; set; }
    public string DriverRecordId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhoneNumber { get; set; } = string.Empty;
    public string DriveNo { get; set; } = string.Empty;
    public string DrivePlate { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string OnlineTime { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
