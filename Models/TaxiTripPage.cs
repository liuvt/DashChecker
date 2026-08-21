namespace DashChecker.Models;

public sealed record TaxiTripListItem(
    long Id,
    int RowOrder,
    string SoHieu,
    string BienSo,
    DateTime BatDau,
    DateTime KetThuc,
    decimal KmCoKhach,
    decimal KmRong,
    decimal TongKm,
    decimal ThanhTien,
    string DiemDau,
    string DiemCuoi,
    string DriverNames,
    string DriverEmployeeCodes,
    string DriverPhones,
    string ShiftSoTai,
    int DriverCount,
    string DriverMatchStatus,
    DateTime? DriverShiftStartAt,
    DateTime? DriverShiftNextAt,
    string AppUserName,
    string AppVehicleNo,
    string AppVehicleCode,
    string AppTripId,
    string AppMatchStatus)
{
    public TimeSpan ThoiLuong => KetThuc >= BatDau ? KetThuc - BatDau : TimeSpan.Zero;
}

public sealed record TaxiTripArchiveListItem(
    long Id,
    DateTime CreatedAt,
    DateTime SavedAt,
    int RowOrder,
    string SoHieu,
    string BienSo,
    DateTime BatDau,
    DateTime KetThuc,
    decimal KmCoKhach,
    decimal KmRong,
    decimal TongKm,
    decimal ThanhTien,
    string DiemDau,
    string DiemCuoi,
    string DriverNames,
    string DriverEmployeeCodes,
    string DriverPhones,
    string ShiftSoTai,
    int DriverCount,
    string DriverMatchStatus,
    DateTime? DriverShiftStartAt,
    DateTime? DriverShiftNextAt,
    string AppUserName,
    string AppVehicleNo,
    string AppVehicleCode,
    string AppTripId,
    string AppMatchStatus)
{
    public TimeSpan ThoiLuong => KetThuc >= BatDau ? KetThuc - BatDau : TimeSpan.Zero;
}

public sealed record TaxiTripSummary(
    int TongCuoc,
    int SoXe,
    decimal TongDoanhThu,
    decimal TongKmCoKhach,
    decimal TongKmRong,
    decimal TongKm);

public sealed record CurrentTaxiTripPage(
    string AreaCode,
    string AreaName,
    long SyncId,
    string FileId,
    DateTime FromDate,
    DateTime ToDate,
    TimeSpan FromTime,
    TimeSpan ToTime,
    DateTime CreatedAt,
    DateTime? SavedAt,
    IReadOnlyList<TaxiTripListItem> Rows,
    TaxiTripSummary Summary,
    int TotalRows,
    int Page,
    int PageSize,
    string Search)
{
    public bool IsSaved => SavedAt.HasValue;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
}

public sealed record ArchiveTaxiTripPage(
    string AreaCode,
    string AreaName,
    IReadOnlyList<TaxiTripArchiveListItem> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    string Search)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
}
