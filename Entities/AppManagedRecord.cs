namespace DashChecker.Entities;

public sealed class AppManagedRecord
{
    public long Id { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public int RowOrder { get; set; }
    public string DataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? SavedAt { get; set; }
}
