namespace DashChecker.Entities;

public sealed class OnlineVehicleCurrent
{
    public long Id { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string PlateNo { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;
    public string VehicleCode { get; set; } = string.Empty;
    public DateTime? GpsDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
