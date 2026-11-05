namespace DashChecker.Entities;

public sealed class SkySoftLinkAccount
{
    public int Id { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;

    // Cấu hình Authentication App (go.skysoft.vn)
    public string AppBaseUrl { get; set; } = "https://go.skysoft.vn/";
    public string UserName { get; set; } = string.Empty;
    public string ProtectedPassword { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = "chrome";
    public string DeviceBrand { get; set; } = "Google Inc.";
    public string DeviceModel { get; set; } = string.Empty;
    public string AppOs { get; set; } = "web";
    public string OsVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "1.0.58";

    // Cấu hình API Liên kết (data.skysoft.vn - GPS Online & Cuốc khách)
    public string ApiBaseUrl { get; set; } = "https://data.skysoft.vn/rest/api/";
    public string ApiUserName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ProtectedApiToken { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 300;
    public decimal MinOccupiedKm { get; set; } = 19.49m;
    public int MatchToleranceMinutes { get; set; } = 15;

    // Thông tin phụ & trạng thái kết nối
    public string Notes { get; set; } = string.Empty;
    public bool FilterCrossProvinceDuplicates { get; set; } = true;
    public bool IsConnected { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public string LastStatusMessage { get; set; } = "Chưa kết nối";
    public DateTime UpdatedAt { get; set; }
}
