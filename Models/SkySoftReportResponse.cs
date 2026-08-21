using System.Text.Json.Serialization;

namespace DashChecker.Models;

public sealed class SkySoftReportResponse
{
    [JsonPropertyName("reportID")]
    public int ReportID { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("fileID")]
    public string? FileID { get; set; }

    [JsonPropertyName("actionResult")]
    public string? ActionResult { get; set; }

    [JsonPropertyName("xlsxFormat")]
    public bool XlsxFormat { get; set; }
}
