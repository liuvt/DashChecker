using System.Text.Json.Serialization;

namespace DashChecker.Models;

public sealed class SkySoftLoginRequest
{
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("deviceID")]
    public string DeviceID { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = "chrome";

    [JsonPropertyName("deviceModel")]
    public string DeviceModel { get; set; } = string.Empty;

    [JsonPropertyName("deviceBrand")]
    public string DeviceBrand { get; set; } = "Google Inc.";

    [JsonPropertyName("appOs")]
    public string AppOs { get; set; } = "web";

    [JsonPropertyName("osVersion")]
    public string OsVersion { get; set; } = string.Empty;

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.58";

    [JsonPropertyName("fireBaseToken")]
    public string FireBaseToken { get; set; } = string.Empty;

    [JsonPropertyName("reconnect")]
    public bool Reconnect { get; set; }
}
