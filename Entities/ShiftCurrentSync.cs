namespace DashChecker.Entities;

public sealed class ShiftCurrentSync
{
    public long Id { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string SourceUserName { get; set; } = string.Empty;
    public string SpreadsheetId { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public DateTime SourceDate { get; set; }
    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SavedAt { get; set; }
    public ICollection<ShiftCurrent> Rows { get; set; } = new List<ShiftCurrent>();
}
