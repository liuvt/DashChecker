namespace DashChecker.Entities;

public sealed class TaxiTripArchiveSync
{
    public long Id { get; set; }
    public long? LegacySyncId { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string SourceUserName { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public int ReportId { get; set; } = 32;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public TimeSpan FromTime { get; set; }
    public TimeSpan ToTime { get; set; }
    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime SavedAt { get; set; }
    public ICollection<TaxiTripArchive> Trips { get; set; } = new List<TaxiTripArchive>();
}
