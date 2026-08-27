namespace DashChecker.Models;

public sealed class GgsDashCheckerOptions
{
    public string SpreadsheetId { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "DashChecker - GgsDashChecker";
}
