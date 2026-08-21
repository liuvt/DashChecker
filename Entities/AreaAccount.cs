namespace DashChecker.Entities;

public sealed class AreaAccount
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string AreaCode { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
