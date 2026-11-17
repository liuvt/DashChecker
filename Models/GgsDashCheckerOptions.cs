namespace DashChecker.Models;

public sealed class GgsDashCheckerOptions
{
    public string SpreadsheetId { get; set; } = "14bKnC2HOWsRxveD2vSK6G2W5KGi1wxMYMyggiYU2w9Y";
    public string SheetName { get; set; } = "Cuốc Xe Đồng Hồ (Thô)";
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "DashChecker - GgsDashChecker";
}
