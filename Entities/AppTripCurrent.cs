namespace DashChecker.Entities;

public sealed class AppTripCurrent
{
    public long Id { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public int RowOrder { get; set; }
    public string GeneratedId { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;
    public string PlateNo { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime PickupDate { get; set; }
    public DateTime? DropOffDate { get; set; }
    public decimal Km { get; set; }
    public decimal EmptyKm { get; set; }
    public decimal TotalKm { get; set; }
    public decimal WaitTimeMinutes { get; set; }
    public decimal WaitCharge { get; set; }
    public decimal Charge { get; set; }
    public decimal RealCharge { get; set; }
    public string TripId { get; set; } = string.Empty;
    public string FromPlaceName { get; set; } = string.Empty;
    public string ToPlaceName { get; set; } = string.Empty;
    public string DatePart { get; set; } = string.Empty;
    public string TimePart { get; set; } = string.Empty;
    public string VehicleDateKey { get; set; } = string.Empty;
    public string VehicleCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
