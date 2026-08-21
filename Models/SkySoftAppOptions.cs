namespace DashChecker.Models;

public sealed class SkySoftAppOptions
{
    public string BaseUrl { get; set; } = "https://data.skysoft.vn/rest/api/";
    public int TimeoutSeconds { get; set; } = 300;
    public decimal MinOccupiedKm { get; set; } = 19.49m;
    public int MatchToleranceMinutes { get; set; } = 15;
    public Dictionary<string, SkySoftAppAreaOptions> Areas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SkySoftAppAreaOptions
{
    public string Username { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
