namespace DashChecker.Models;

public sealed class GoogleShiftOptions
{
    public string SpreadsheetId { get; set; } = "169wRJ7BvJxH7bAuQwebYA16WGSKTYo0oJSM0gcwf5U0";
    public int Gid { get; set; } = 0;
    public string SheetName { get; set; } = "QL_LEN_XUONG_CA";
    public int TimeoutSeconds { get; set; } = 180;

    // Không commit credential thật vào source. Có thể dùng file JSON trong App_Data
    // hoặc đặt toàn bộ JSON bằng User Secrets / biến môi trường GoogleShift__ServiceAccountJson.
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "DashChecker";
}
