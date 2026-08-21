namespace DashChecker.Models;

public sealed record OnlineVehicleStoredRow(
    OnlineVehicleInfo Vehicle,
    DateTime CreatedAt,
    DateTime? SavedAt);

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
