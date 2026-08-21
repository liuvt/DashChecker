using System.Text.Json.Serialization;

namespace DashChecker.Models;

public sealed class SkySoftReportRequest
{
    [JsonPropertyName("reportID")]
    public int ReportID { get; set; } = 32;

    [JsonPropertyName("plateNo")]
    public string PlateNo { get; set; } = string.Empty;

    [JsonPropertyName("fromDate")]
    public string FromDate { get; set; } = string.Empty;

    [JsonPropertyName("toDate")]
    public string ToDate { get; set; } = string.Empty;

    [JsonPropertyName("fromTime")]
    public string FromTime { get; set; } = "05:00:00";

    [JsonPropertyName("toTime")]
    public string ToTime { get; set; } = "05:00:00";

    [JsonPropertyName("userID")]
    public int UserID { get; set; }

    [JsonPropertyName("groupID")]
    public int GroupID { get; set; }

    [JsonPropertyName("placeGroupID")]
    public int PlaceGroupID { get; set; }

    [JsonPropertyName("lineID")]
    public int LineID { get; set; }

    [JsonPropertyName("jsonOutput")]
    public bool JsonOutput { get; set; } = true;

    [JsonPropertyName("filterByEndDate")]
    public bool FilterByEndDate { get; set; }

    [JsonPropertyName("vehicleIDs")]
    public int[] VehicleIDs { get; set; } = [];

    [JsonPropertyName("gzipOutput")]
    public bool GzipOutput { get; set; } = true;
}
