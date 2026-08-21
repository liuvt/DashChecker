namespace DashChecker.Entities;

public sealed class LocalCredential
{
    public int Id { get; set; } = 1;
    public string UserName { get; set; } = string.Empty;
    public string ProtectedPassword { get; set; } = string.Empty;
    public bool RememberPassword { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
