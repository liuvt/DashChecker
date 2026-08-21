using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class SkySoftAppService
{
    private readonly HttpClient _httpClient;
    private readonly SkySoftAppOptions _options;

    public int MatchToleranceMinutes => Math.Clamp(_options.MatchToleranceMinutes, 1, 120);

    public SkySoftAppService(HttpClient httpClient, IOptions<SkySoftAppOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(30, _options.TimeoutSeconds));
    }

    public async Task<IReadOnlyList<OnlineVehicleInfo>> GetOnlineVehiclesAsync(
        string areaCode,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccount(areaCode);
        using var document = await PostAsync("online_taxi", account, new { }, cancellationToken);

        if (!document.RootElement.TryGetProperty("vehicles", out var vehiclesElement) ||
            vehiclesElement.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<OnlineVehicleInfo>();
        foreach (var vehicle in vehiclesElement.EnumerateArray())
        {
            result.Add(new OnlineVehicleInfo(
                GetString(vehicle, "vehicleID"),
                GetString(vehicle, "plateNo"),
                GetString(vehicle, "vehicleNo"),
                GetString(vehicle, "vehicleCode"),
                ParseApiDate(GetString(vehicle, "gpsDate")),
                ParseApiDate(GetString(vehicle, "updateDate"))));
        }

        return result
            .OrderBy(x => x.VehicleNo, NaturalStringComparer.Instance)
            .ThenBy(x => x.PlateNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Bám đúng flow Apps Script fetchAndProcessTrips:
    /// 1) online_taxi để map plateNo -> vehicleNo.
    /// 2) query_taxi_trips đúng 3 ngày i=0,1,2.
    /// 3) i=0: loại pickupDate > 05:00 hôm nay.
    /// 4) i=2: loại dropOffDate < 05:00 hôm qua.
    /// 5) loại km <= 19.49.
    /// 6) group theo vehicleNo; cuốc không map được vehicleNo bị loại như vehicleNoGroupby.
    /// 7) sort vehicleNo tự nhiên, trong xe sort theo dropOffDate.
    /// </summary>
    public async Task<OnlineTripResult> GetThreeDayFlowTripsAsync(
        string areaCode,
        CancellationToken cancellationToken = default)
    {
        var account = ResolveAccount(areaCode);
        var now = VietnamClock.Now;
        var targetToday05 = now.Date.AddHours(5);
        var targetYesterday05 = now.Date.AddDays(-1).AddHours(5);

        // Đúng flow gốc: gọi online_taxi trước khi query trips.
        var vehicles = await GetOnlineVehiclesAsync(areaCode, cancellationToken);
        var collected = new List<OnlineTripInfo>();
        var generatedId = 1;

        for (var i = 0; i <= 2; i++)
        {
            var queryDate = now.Date.AddDays(-i);
            var yyyyMMdd = queryDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            using var document = await PostAsync(
                "query_taxi_trips",
                account,
                new { date = yyyyMMdd },
                cancellationToken);

            if (!document.RootElement.TryGetProperty("trips", out var tripsElement) ||
                tripsElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var trip in tripsElement.EnumerateArray())
            {
                var pickup = ParseApiDate(GetString(trip, "pickupDate"));
                var dropOff = ParseApiDate(GetString(trip, "dropOffDate"));

                // Code gốc chỉ kiểm tra khi có pickupDate/dropOffDate.
                if (i == 0 && pickup.HasValue && pickup.Value > targetToday05)
                    continue;

                if (i == 2 && dropOff.HasValue && dropOff.Value < targetYesterday05)
                    continue;

                // Dữ liệu xuất cuối cùng cần pickupDate để tạo datePart/timePart.
                if (!pickup.HasValue)
                    continue;

                var km = ConvertRawKm(GetDecimal(trip, "km"));
                if (km <= 19.49m)
                    continue;

                var emptyKm = ConvertRawKm(GetDecimal(trip, "emptyKm"));
                var plateNo = GetString(trip, "plateNo");

                // Đúng flow JS: _online.find(v => v[1] === trip.plateNo)
                var vehicle = vehicles.FirstOrDefault(v =>
                    string.Equals(v.PlateNo, plateNo, StringComparison.Ordinal));

                var charge = GetDecimal(trip, "charge");
                var realChargeRaw = GetDecimal(trip, "realCharge", -1m);
                var realCharge = realChargeRaw == -1m ? charge : realChargeRaw;
                var waitTime = Math.Round(
                    GetDecimal(trip, "waitTime") * 100m / 60m,
                    MidpointRounding.AwayFromZero) / 100m;

                collected.Add(new OnlineTripInfo(
                    $"ID_{generatedId:0000}",
                    vehicle?.VehicleNo ?? string.Empty,
                    plateNo,
                    GetString(trip, "userName"),
                    pickup.Value,
                    dropOff,
                    km,
                    emptyKm,
                    km + emptyKm,
                    waitTime,
                    GetDecimal(trip, "waitCharge"),
                    charge,
                    realCharge,
                    GetString(trip, "_id"),
                    GetString(trip, "fromPlaceName"),
                    GetString(trip, "toPlaceName"),
                    vehicle?.VehicleCode ?? string.Empty));

                generatedId++;
            }
        }

        // vehicleNoGroupby() trong Apps Script bỏ các dòng không có vehicleNo.
        var finalRows = collected
            .Where(x => !string.IsNullOrWhiteSpace(x.VehicleNo))
            .GroupBy(x => x.VehicleNo, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, NaturalStringComparer.Instance)
            .SelectMany(g => g
                .OrderBy(x => x.DropOffDate ?? DateTime.MaxValue)
                .ThenBy(x => x.PickupDate))
            .ToList();

        return new OnlineTripResult(
            areaCode,
            targetYesterday05,
            targetToday05,
            vehicles,
            finalRows);
    }

    // Giữ API tên cũ để các phần ghép SkySoft Current không bị gãy.
    public Task<OnlineTripResult> GetCurrentOperationalTripsAsync(
        string areaCode,
        CancellationToken cancellationToken = default) =>
        GetThreeDayFlowTripsAsync(areaCode, cancellationToken);

    private SkySoftAppAreaOptions ResolveAccount(string areaCode)
    {
        var pair = _options.Areas.FirstOrDefault(x =>
            string.Equals(x.Key, areaCode, StringComparison.OrdinalIgnoreCase));
        var account = pair.Value;

        if (account is null || string.IsNullOrWhiteSpace(account.Username))
            throw new InvalidOperationException($"Online App chưa cấu hình tài khoản API cho khu vực {areaCode}.");
        if (string.IsNullOrWhiteSpace(account.Key) || string.IsNullOrWhiteSpace(account.Token))
            throw new InvalidOperationException(
                $"Online App khu vực {areaCode} chưa có Key/Token. Hãy cấu hình bằng User Secrets hoặc biến môi trường.");
        return account;
    }

    private async Task<JsonDocument> PostAsync(
        string endpoint,
        SkySoftAppAreaOptions account,
        object payload,
        CancellationToken cancellationToken)
    {
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var md5 = BuildMd5(account.Username, unixTime, account.Key);
        var url = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("time", unixTime.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("md5", md5);
        request.Headers.TryAddWithoutValidation("token", account.Token);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Online App {endpoint} lỗi HTTP {(int)response.StatusCode}: {Limit(body, 800)}");

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Online App {endpoint} trả dữ liệu không phải JSON: {Limit(body, 500)}", ex);
        }
    }

    private static string BuildMd5(string username, long unixTime, string key)
    {
        var bytes = Encoding.UTF8.GetBytes($"{username}-{unixTime}-{key}");
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static decimal ConvertRawKm(decimal raw) =>
        Math.Round(raw / 1000m, 2, MidpointRounding.AwayFromZero);

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static decimal GetDecimal(JsonElement element, string property, decimal defaultValue = 0m)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return defaultValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            return number;
        return defaultValue;
    }

    private static DateTime? ParseApiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            try
            {
                var dto = value.Length >= 13
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
                return VietnamClock.FromUtc(dto.UtcDateTime);
            }
            catch (ArgumentOutOfRangeException) { }
        }

        var hasExplicitOffset = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
        if (hasExplicitOffset && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var dtoWithOffset))
            return VietnamClock.FromUtc(dtoWithOffset.UtcDateTime);

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);

        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.AllowWhiteSpaces, out date))
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);

        return null;
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public static NaturalStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            var ix = 0;
            var iy = 0;

            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    var sx = ix;
                    var sy = iy;
                    while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                    while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                    var nx = x[sx..ix].TrimStart('0');
                    var ny = y[sy..iy].TrimStart('0');
                    if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                    var numCompare = string.Compare(nx, ny, StringComparison.Ordinal);
                    if (numCompare != 0) return numCompare;
                    continue;
                }

                var cx = char.ToUpperInvariant(x[ix]);
                var cy = char.ToUpperInvariant(y[iy]);
                if (cx != cy) return cx.CompareTo(cy);
                ix++;
                iy++;
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
