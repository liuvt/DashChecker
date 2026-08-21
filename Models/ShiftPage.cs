namespace DashChecker.Models;

public sealed record ShiftListItem(
    long Id,
    int SourceRow,
    DateTime SourceDate,
    string SoTai,
    string SoCho,
    string BienKiemSoat,
    string HoTenMsnv,
    string DriverName,
    string EmployeeCode,
    string DriverPhone,
    string TrangThaiLenXuongCa,
    string LoaiHinhHopTac,
    string HinhThucKinhDoanh,
    string LyDoXuongCa,
    string GhiChu,
    TimeSpan SourceTime,
    DateTime SourceAt,
    string HinhThucLuong,
    bool IsActive,
    int DriverCountForVehicle,
    int RowCountForSoTai);

public sealed record ShiftArchiveListItem(
    long Id,
    DateTime CreatedAt,
    DateTime SavedAt,
    int SourceRow,
    DateTime SourceDate,
    string SoTai,
    string SoCho,
    string BienKiemSoat,
    string HoTenMsnv,
    string DriverName,
    string EmployeeCode,
    string DriverPhone,
    string TrangThaiLenXuongCa,
    string LoaiHinhHopTac,
    string HinhThucKinhDoanh,
    string LyDoXuongCa,
    string GhiChu,
    TimeSpan SourceTime,
    DateTime SourceAt,
    string HinhThucLuong,
    bool IsActive,
    int DriverCountForVehicle,
    int RowCountForSoTai);

public sealed record ShiftSummary(
    int TotalRows,
    int LenCa,
    int XuongCa,
    int SoXe,
    int TaiXe,
    int XeNhieuTai);

public sealed record CurrentShiftPage(
    string AreaCode,
    string AreaName,
    long SyncId,
    DateTime SourceDate,
    DateTime CreatedAt,
    DateTime? SavedAt,
    IReadOnlyList<ShiftListItem> Rows,
    ShiftSummary Summary,
    int TotalRows,
    int Page,
    int PageSize,
    string Search)
{
    public bool IsSaved => SavedAt.HasValue;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
}

public sealed record ArchiveShiftPage(
    string AreaCode,
    string AreaName,
    IReadOnlyList<ShiftArchiveListItem> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    string Search)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
}
