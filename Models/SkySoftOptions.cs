namespace DashChecker.Models;

public sealed class SkySoftOptions
{
    public string BaseUrl { get; set; } = "https://go.skysoft.vn/";
    public string DeviceName { get; set; } = "chrome";
    public string AppVersion { get; set; } = "1.0.58";
    // HttpClient dùng timeout vô hạn; timeout nghiệp vụ được áp riêng cho POST/GET SkySoft.
    public int ReportTimeoutMinutes { get; set; } = 30;
    public int ExcelTimeoutMinutes { get; set; } = 30;
    public int ReportRetryCount { get; set; } = 3;
    public int ReportRetryDelaySeconds { get; set; } = 10;
}
