using System.Globalization;
using DashChecker.Services;

namespace DashChecker.Models;

public sealed record OnlineVehicleStoredRow(
    OnlineVehicleInfo Vehicle,
    DateTime CreatedAt,
    DateTime? SavedAt)
{
    public static DateTime CurrentShiftThreshold => VietnamClock.Now.Date.AddHours(5);

    public bool HasActiveGpsSignal =>
        Vehicle.GpsDate.HasValue && Vehicle.GpsDate.Value >= CurrentShiftThreshold;

    public string GpsSignalText =>
        HasActiveGpsSignal ? "Có tín hiệu" : "Mất tín hiệu";

    /// <summary>
    /// Map đúng 8 cột A..H khi đẩy dữ liệu GPS Online vào Google Sheet (sheetName: GPS):
    /// Cột A: Vehicle ID
    /// Cột B: Biển số
    /// Cột C: Số tài
    /// Cột D: Vehicle Code
    /// Cột E: Tín hiệu GPS
    /// Cột F: Thời gian tạo
    /// Cột G: GPS Date
    /// Cột H: Update Date
    /// </summary>
    public IReadOnlyList<object?> ToGoogleSheetValues() => new object?[]
    {
        Vehicle.VehicleId,
        Vehicle.PlateNo,
        Vehicle.VehicleNo,
        Vehicle.VehicleCode,
        GpsSignalText,
        CreatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        Vehicle.GpsDate?.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
        Vehicle.UpdateDate?.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty
    };
}

public sealed record AppTripStoredRow(
    OnlineTripInfo Trip,
    DateTime CreatedAt,
    DateTime? SavedAt);

public sealed record OnlineVehicleSnapshot(
    string AreaCode,
    DateTime? CreatedAt,
    DateTime? SavedAt,
    IReadOnlyList<OnlineVehicleStoredRow> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    string Search)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)Math.Max(1, PageSize)));
}

public sealed record AppTripSnapshot(
    string AreaCode,
    DateTime? CreatedAt,
    DateTime? SavedAt,
    DateTime FromAt,
    DateTime ToAt,
    IReadOnlyList<AppTripStoredRow> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    string Search)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)Math.Max(1, PageSize)));
}
