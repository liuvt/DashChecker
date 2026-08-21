namespace DashChecker.Entities;

public sealed class ShiftArchive
{
    public long Id { get; set; }
    public long SyncId { get; set; }
    public ShiftArchiveSync? Sync { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public int SourceRow { get; set; }
    public DateTime SourceDate { get; set; }
    public string SoTai { get; set; } = string.Empty;
    public string SoCho { get; set; } = string.Empty;
    public string BienKiemSoat { get; set; } = string.Empty;
    public string BienKiemSoatNormalized { get; set; } = string.Empty;
    public string HoTenMsnv { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;
    public string TrangThaiLenXuongCa { get; set; } = string.Empty;
    public string LoaiHinhHopTac { get; set; } = string.Empty;
    public string HinhThucKinhDoanh { get; set; } = string.Empty;
    public string LyDoXuongCa { get; set; } = string.Empty;
    public string GhiChu { get; set; } = string.Empty;
    public TimeSpan SourceTime { get; set; }
    public DateTime SourceAt { get; set; }
    public string HinhThucLuong { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime SavedAt { get; set; }
}
