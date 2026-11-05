namespace DashChecker.Entities;

public sealed class SharedVehicleRegistry
{
    public long Id { get; set; }
    public string PlateNo { get; set; } = string.Empty;
    public string PlateNoNormalized { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;
    public string VehicleNoNormalized { get; set; } = string.Empty;
    public string VehicleKeyComposite { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string VehicleCode { get; set; } = string.Empty;

    // Khu vực sở hữu chuẩn & danh sách các tài khoản/tỉnh trả về xe này
    public string PrimaryAreaCode { get; set; } = string.Empty;
    public string SourceAreas { get; set; } = string.Empty;
    public int DuplicateCount { get; set; } = 1;

    public DateTime? GpsDate { get; set; }
    public DateTime? UpdateDate { get; set; }

    // Đánh dấu loại bỏ xe/thông tin không cần thiết hoặc trùng tỉnh khác
    public bool IsExcluded { get; set; }
    public string ExcludeReason { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
