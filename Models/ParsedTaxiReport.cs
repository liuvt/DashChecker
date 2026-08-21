namespace DashChecker.Models;

public sealed record ParsedTaxiReport(
    string FileId,
    IReadOnlyList<ParsedTaxiTrip> Trips,
    DateTime FromDate,
    DateTime ToDate,
    TimeSpan FromTime,
    TimeSpan ToTime);
