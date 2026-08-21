namespace DashChecker.Entities;

public sealed class TaxiTripSync
{
    public long Id { get; set; }
    public string FileId { get; set; } = string.Empty;
    public int ReportId { get; set; } = 32;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public TimeSpan FromTime { get; set; }
    public TimeSpan ToTime { get; set; }

    // Giữ 2 cột này để tương thích database của bản trước.
    public string ColumnNamesJson { get; set; } = "[]";
    public int ColumnCount { get; set; } = 10;

    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<TaxiTrip> Trips { get; set; } = new List<TaxiTrip>();
}
