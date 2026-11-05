namespace DashChecker.Models;

public sealed record SkySoftLinkAccountView(
    int Id,
    string AreaCode,
    string AreaName,
    string AppBaseUrl,
    string UserName,
    bool HasPassword,
    string DeviceId,
    string DeviceName,
    string DeviceBrand,
    string DeviceModel,
    string AppOs,
    string OsVersion,
    string AppVersion,
    string ApiBaseUrl,
    string ApiUserName,
    string ApiKey,
    bool HasApiToken,
    int TimeoutSeconds,
    decimal MinOccupiedKm,
    int MatchToleranceMinutes,
    string Notes,
    bool FilterCrossProvinceDuplicates,
    bool IsConnected,
    bool HasLiveAppCookie,
    DateTime? LastConnectedAt,
    string LastStatusMessage,
    DateTime UpdatedAt)
{
    public bool HasAppCredentials => !string.IsNullOrWhiteSpace(UserName) && HasPassword;
    public bool HasApiCredentials => !string.IsNullOrWhiteSpace(ApiUserName) && !string.IsNullOrWhiteSpace(ApiKey) && HasApiToken;
}

public sealed class SkySoftLinkEditModel
{
    public int? Id { get; set; }
    public string AreaCode { get; set; } = "BL";
    public string AreaName { get; set; } = "Bạc Liêu";

    // Authentication App (go.skysoft.vn)
    public string AppBaseUrl { get; set; } = "https://go.skysoft.vn/";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool KeepExistingPassword { get; set; } = true;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = "chrome";
    public string DeviceBrand { get; set; } = "Google Inc.";
    public string DeviceModel { get; set; } = string.Empty;
    public string AppOs { get; set; } = "web";
    public string OsVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "1.0.58";

    // API Liên kết (data.skysoft.vn)
    public string ApiBaseUrl { get; set; } = "https://data.skysoft.vn/rest/api/";
    public string ApiUserName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public bool KeepExistingApiToken { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 300;
    public decimal MinOccupiedKm { get; set; } = 19.49m;
    public int MatchToleranceMinutes { get; set; } = 15;

    // Thông tin phụ
    public string Notes { get; set; } = string.Empty;
    public bool FilterCrossProvinceDuplicates { get; set; } = true;

    public static SkySoftLinkEditModel CreateNew() => new()
    {
        DeviceId = Guid.NewGuid().ToString("N")
    };

    public static SkySoftLinkEditModel FromView(SkySoftLinkAccountView view) => new()
    {
        Id = view.Id,
        AreaCode = view.AreaCode,
        AreaName = view.AreaName,
        AppBaseUrl = view.AppBaseUrl,
        UserName = view.UserName,
        Password = string.Empty,
        KeepExistingPassword = view.HasPassword,
        DeviceId = view.DeviceId,
        DeviceName = view.DeviceName,
        DeviceBrand = view.DeviceBrand,
        DeviceModel = view.DeviceModel,
        AppOs = view.AppOs,
        OsVersion = view.OsVersion,
        AppVersion = view.AppVersion,
        ApiBaseUrl = view.ApiBaseUrl,
        ApiUserName = view.ApiUserName,
        ApiKey = view.ApiKey,
        ApiToken = string.Empty,
        KeepExistingApiToken = view.HasApiToken,
        TimeoutSeconds = view.TimeoutSeconds,
        MinOccupiedKm = view.MinOccupiedKm,
        MatchToleranceMinutes = view.MatchToleranceMinutes,
        Notes = view.Notes,
        FilterCrossProvinceDuplicates = view.FilterCrossProvinceDuplicates
    };
}

public sealed record SharedVehicleItemView(
    long Id,
    string PlateNo,
    string VehicleNo,
    string VehicleKeyComposite,
    string VehicleId,
    string VehicleCode,
    string PrimaryAreaCode,
    string SourceAreas,
    int DuplicateCount,
    DateTime? GpsDate,
    DateTime? UpdateDate,
    bool IsExcluded,
    string ExcludeReason,
    DateTime FetchedAt)
{
    public bool IsSharedAcrossProvinces => DuplicateCount > 1;
}

public sealed record SharedVehicleFetchSummary(
    int ConnectedAccountsCalled,
    int RawVehiclesFetched,
    int UniqueVehiclesStored,
    int CrossProvinceDuplicatesRemoved,
    int ExcludedVehiclesCount,
    int ActiveVehiclesCount,
    IReadOnlyList<string> AccountLogs);

public sealed record SharedVehicleRegistryPage(
    IReadOnlyList<SharedVehicleItemView> Rows,
    int TotalRows,
    int TotalUniqueVehicles,
    int MultiProvinceCount,
    int ExcludedCount,
    int ActiveCleanCount,
    int Page,
    int PageSize,
    string Search,
    string FilterMode,
    string AreaFilter)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)Math.Max(1, PageSize)));
}
