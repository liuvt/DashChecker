namespace DashChecker.Entities;

public sealed class TaxiTripArchive
{
    public long Id { get; set; }
    public long SyncId { get; set; }
    public TaxiTripArchiveSync? Sync { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public int RowOrder { get; set; }
    public string SoHieu { get; set; } = string.Empty;
    public string BienSo { get; set; } = string.Empty;
    public DateTime BatDau { get; set; }
    public DateTime KetThuc { get; set; }
    public decimal KmCoKhach { get; set; }
    public decimal KmRong { get; set; }
    public decimal TongKm { get; set; }
    public decimal ThanhTien { get; set; }
    public string DiemDau { get; set; } = string.Empty;
    public string DiemCuoi { get; set; } = string.Empty;
    public string DriverNames { get; set; } = string.Empty;
    public string DriverEmployeeCodes { get; set; } = string.Empty;
    public string DriverPhones { get; set; } = string.Empty;
    public string ShiftSoTai { get; set; } = string.Empty;
    public int DriverCount { get; set; }
    public string DriverMatchStatus { get; set; } = string.Empty;
    public DateTime? DriverShiftStartAt { get; set; }
    public DateTime? DriverShiftNextAt { get; set; }
    public string AppUserName { get; set; } = string.Empty;
    public string AppVehicleNo { get; set; } = string.Empty;
    public string AppVehicleCode { get; set; } = string.Empty;
    public string AppTripId { get; set; } = string.Empty;
    public string AppMatchStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime SavedAt { get; set; }
}
