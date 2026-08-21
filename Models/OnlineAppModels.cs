namespace DashChecker.Models;

public sealed record OnlineVehicleInfo(
    string VehicleId,
    string PlateNo,
    string VehicleNo,
    string VehicleCode,
    DateTime? GpsDate,
    DateTime? UpdateDate);

public sealed record OnlineTripInfo(
    string GeneratedId,
    string VehicleNo,
    string PlateNo,
    string UserName,
    DateTime PickupDate,
    DateTime? DropOffDate,
    decimal Km,
    decimal EmptyKm,
    decimal TotalKm,
    decimal WaitTimeMinutes,
    decimal WaitCharge,
    decimal Charge,
    decimal RealCharge,
    string TripId,
    string FromPlaceName,
    string ToPlaceName,
    string VehicleCode)
{
    public string DatePart => PickupDate.ToString("dd/MM/yyyy");
    public string TimePart => PickupDate.ToString("HH:mm:ss");
    public string VehicleDateKey => string.IsNullOrWhiteSpace(VehicleNo) ? string.Empty : $"{VehicleNo} - {DatePart}";

    public TimeSpan Duration => DropOffDate.HasValue && DropOffDate.Value >= PickupDate
        ? DropOffDate.Value - PickupDate
        : TimeSpan.Zero;
}

public sealed record OnlineTripResult(
    string AreaCode,
    DateTime FromAt,
    DateTime ToAt,
    IReadOnlyList<OnlineVehicleInfo> Vehicles,
    IReadOnlyList<OnlineTripInfo> Trips);

public sealed record OnlineAppApplyResult(int CurrentTrips, int MatchedTrips);
