using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class SkySoftLinkService : IDisposable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly SkySoftOptions _skySoftOptions;
    private readonly SkySoftAppOptions _skySoftAppOptions;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<int, LiveAppSession> _liveSessions = new();
    private bool _seeded;

    public event Action? Changed;

    public SkySoftLinkService(
        IDbContextFactory<LocalDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<SkySoftOptions> skySoftOptions,
        IOptions<SkySoftAppOptions> skySoftAppOptions)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector("DashChecker.SkySoftLink.v1");
        _skySoftOptions = skySoftOptions.Value;
        _skySoftAppOptions = skySoftAppOptions.Value;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (_seeded) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_seeded) return;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var existingCount = await db.SkySoftLinkAccounts.CountAsync(cancellationToken);
            if (existingCount > 0)
            {
                _seeded = true;
                return;
            }

            var defaultAreas = new (string Code, string Name, string DefaultUser)[]
            {
                ("BL", "Bạc Liêu", "namthangbl"),
                ("VL", "Vĩnh Long", "namthangvl"),
                ("RG", "Rạch Giá", "namthangrg"),
                ("CM", "Cà Mau", "namthangcm"),
                ("CT", "Cần Thơ", "namthangct")
            };

            var now = VietnamClock.Now;
            foreach (var (code, name, defaultUser) in defaultAreas)
            {
                _skySoftAppOptions.Areas.TryGetValue(code, out var apiOpt);
                var entity = new SkySoftLinkAccount
                {
                    AreaCode = code,
                    AreaName = name,
                    AppBaseUrl = string.IsNullOrWhiteSpace(_skySoftOptions.BaseUrl)
                        ? "https://go.skysoft.vn/"
                        : _skySoftOptions.BaseUrl,
                    UserName = defaultUser,
                    ProtectedPassword = string.Empty,
                    DeviceId = Guid.NewGuid().ToString("N"),
                    DeviceName = string.IsNullOrWhiteSpace(_skySoftOptions.DeviceName) ? "chrome" : _skySoftOptions.DeviceName,
                    DeviceBrand = "Google Inc.",
                    DeviceModel = string.Empty,
                    AppOs = "web",
                    OsVersion = string.Empty,
                    AppVersion = string.IsNullOrWhiteSpace(_skySoftOptions.AppVersion) ? "1.0.58" : _skySoftOptions.AppVersion,
                    ApiBaseUrl = string.IsNullOrWhiteSpace(_skySoftAppOptions.BaseUrl)
                        ? "https://data.skysoft.vn/rest/api/"
                        : _skySoftAppOptions.BaseUrl,
                    ApiUserName = apiOpt?.Username ?? string.Empty,
                    ApiKey = apiOpt?.Key ?? string.Empty,
                    ProtectedApiToken = !string.IsNullOrWhiteSpace(apiOpt?.Token)
                        ? Protect(apiOpt.Token)
                        : string.Empty,
                    TimeoutSeconds = Math.Max(30, _skySoftAppOptions.TimeoutSeconds),
                    MinOccupiedKm = _skySoftAppOptions.MinOccupiedKm > 0 ? _skySoftAppOptions.MinOccupiedKm : 19.49m,
                    MatchToleranceMinutes = Math.Clamp(_skySoftAppOptions.MatchToleranceMinutes, 1, 120),
                    Notes = $"Liên kết mặc định khu vực {name} ({code})",
                    FilterCrossProvinceDuplicates = true,
                    IsConnected = false,
                    LastConnectedAt = null,
                    LastStatusMessage = "Chưa kết nối",
                    UpdatedAt = now
                };

                db.SkySoftLinkAccounts.Add(entity);
            }

            await db.SaveChangesAsync(cancellationToken);
            _seeded = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<SkySoftLinkAccountView>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SkySoftLinkAccounts.AsNoTracking()
            .OrderBy(x => x.AreaCode)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToView).ToList();
    }

    public async Task<int> SaveAccountAsync(
        SkySoftLinkEditModel input,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var areaCode = (input.AreaCode ?? string.Empty).Trim().ToUpperInvariant();
        var areaName = (input.AreaName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(areaCode))
            throw new InvalidOperationException("Mã khu vực (AreaCode) không được để trống.");
        if (string.IsNullOrWhiteSpace(areaName))
            throw new InvalidOperationException("Tên khu vực không được để trống.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        SkySoftLinkAccount entity;

        if (input.Id.HasValue && input.Id.Value > 0)
        {
            entity = await db.SkySoftLinkAccounts
                .FirstOrDefaultAsync(x => x.Id == input.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Không tìm thấy tài khoản liên kết cần cập nhật.");
        }
        else
        {
            entity = new SkySoftLinkAccount
            {
                DeviceId = string.IsNullOrWhiteSpace(input.DeviceId)
                    ? Guid.NewGuid().ToString("N")
                    : input.DeviceId.Trim(),
                LastStatusMessage = "Chưa kết nối"
            };
            db.SkySoftLinkAccounts.Add(entity);
        }

        entity.AreaCode = areaCode;
        entity.AreaName = areaName;
        entity.AppBaseUrl = string.IsNullOrWhiteSpace(input.AppBaseUrl)
            ? "https://go.skysoft.vn/"
            : input.AppBaseUrl.Trim();
        entity.UserName = (input.UserName ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            entity.ProtectedPassword = Protect(input.Password);
        }
        else if (!input.KeepExistingPassword)
        {
            entity.ProtectedPassword = string.Empty;
        }

        entity.DeviceId = string.IsNullOrWhiteSpace(input.DeviceId)
            ? Guid.NewGuid().ToString("N")
            : input.DeviceId.Trim();
        entity.DeviceName = string.IsNullOrWhiteSpace(input.DeviceName) ? "chrome" : input.DeviceName.Trim();
        entity.DeviceBrand = string.IsNullOrWhiteSpace(input.DeviceBrand) ? "Google Inc." : input.DeviceBrand.Trim();
        entity.DeviceModel = (input.DeviceModel ?? string.Empty).Trim();
        entity.AppOs = string.IsNullOrWhiteSpace(input.AppOs) ? "web" : input.AppOs.Trim();
        entity.OsVersion = (input.OsVersion ?? string.Empty).Trim();
        entity.AppVersion = string.IsNullOrWhiteSpace(input.AppVersion) ? "1.0.58" : input.AppVersion.Trim();

        entity.ApiBaseUrl = string.IsNullOrWhiteSpace(input.ApiBaseUrl)
            ? "https://data.skysoft.vn/rest/api/"
            : input.ApiBaseUrl.Trim();
        entity.ApiUserName = (input.ApiUserName ?? string.Empty).Trim();
        entity.ApiKey = (input.ApiKey ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(input.ApiToken))
        {
            entity.ProtectedApiToken = Protect(input.ApiToken.Trim());
        }
        else if (!input.KeepExistingApiToken)
        {
            entity.ProtectedApiToken = string.Empty;
        }

        entity.TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 30, 1800);
        entity.MinOccupiedKm = Math.Max(0m, input.MinOccupiedKm);
        entity.MatchToleranceMinutes = Math.Clamp(input.MatchToleranceMinutes, 1, 120);
        entity.Notes = (input.Notes ?? string.Empty).Trim();
        entity.FilterCrossProvinceDuplicates = input.FilterCrossProvinceDuplicates;
        entity.UpdatedAt = VietnamClock.Now;

        await db.SaveChangesAsync(cancellationToken);

        // Đồng bộ tài khoản vào bảng AreaAccounts để đăng nhập khu vực hoạt động xuyên suốt.
        if (!string.IsNullOrWhiteSpace(entity.UserName))
        {
            var normalizedUser = entity.UserName.Trim().ToLowerInvariant();
            var areaAccount = await db.AreaAccounts
                .FirstOrDefaultAsync(x => x.UserName == normalizedUser, cancellationToken);
            if (areaAccount is null)
            {
                db.AreaAccounts.Add(new AreaAccount
                {
                    UserName = normalizedUser,
                    AreaCode = entity.AreaCode,
                    AreaName = entity.AreaName,
                    IsActive = true,
                    UpdatedAt = VietnamClock.Now
                });
            }
            else
            {
                areaAccount.AreaCode = entity.AreaCode;
                areaAccount.AreaName = entity.AreaName;
                areaAccount.IsActive = true;
                areaAccount.UpdatedAt = VietnamClock.Now;
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        NotifyChanged();
        return entity.Id;
    }

    public async Task DeleteAccountAsync(int id, CancellationToken cancellationToken = default)
    {
        await DisconnectInternalAsync(id);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SkySoftLinkAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is not null)
        {
            db.SkySoftLinkAccounts.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Kết nối (Connect) một tài khoản độc lập:
    /// - Nếu có mật khẩu App (go.skysoft.vn): thực hiện login lấy cookie tokenID.
    /// - Nếu có cấu hình API (data.skysoft.vn): gọi thử online_taxi để xác nhận kết nối.
    /// Cho phép kết nối đồng thời nhiều tài khoản ở nhiều khu vực khác nhau.
    /// </summary>
    public async Task<SkySoftLinkAccountView> ConnectAccountAsync(
        int id,
        string? passwordOverride = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SkySoftLinkAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản liên kết.");

        if (!string.IsNullOrWhiteSpace(passwordOverride))
        {
            entity.ProtectedPassword = Protect(passwordOverride);
        }

        var password = Unprotect(entity.ProtectedPassword);
        var apiToken = Unprotect(entity.ProtectedApiToken);

        var hasAppCreds = !string.IsNullOrWhiteSpace(entity.UserName) && !string.IsNullOrWhiteSpace(password);
        var hasApiCreds = !string.IsNullOrWhiteSpace(entity.ApiUserName) &&
                          !string.IsNullOrWhiteSpace(entity.ApiKey) &&
                          !string.IsNullOrWhiteSpace(apiToken);

        if (!hasAppCreds && !hasApiCreds)
        {
            throw new InvalidOperationException(
                $"Tài khoản {entity.AreaName} ({entity.AreaCode}) chưa có Mật khẩu Authentication App hoặc Key/Token API để kết nối.");
        }

        var statusParts = new List<string>();

        if (hasAppCreds)
        {
            var session = await LoginAppSessionAsync(entity, password, cancellationToken);
            if (_liveSessions.TryRemove(entity.Id, out var old))
                old.Dispose();
            _liveSessions[entity.Id] = session;
            statusParts.Add($"Auth App ({entity.UserName})");
        }

        if (hasApiCreds)
        {
            var vehicles = await CallOnlineTaxiApiAsync(entity, apiToken, cancellationToken);
            statusParts.Add($"API GPS ({vehicles.Count:N0} xe)");
        }

        entity.IsConnected = true;
        entity.LastConnectedAt = VietnamClock.Now;
        entity.LastStatusMessage = "Đã kết nối: " + string.Join(" + ", statusParts);
        entity.UpdatedAt = VietnamClock.Now;
        await db.SaveChangesAsync(cancellationToken);

        NotifyChanged();
        return ToView(entity);
    }

    public async Task<SkySoftLinkAccountView> DisconnectAccountAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await DisconnectInternalAsync(id);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SkySoftLinkAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản liên kết.");

        entity.IsConnected = false;
        entity.LastStatusMessage = $"Đã ngắt kết nối lúc {VietnamClock.Now:HH:mm:ss}";
        entity.UpdatedAt = VietnamClock.Now;
        await db.SaveChangesAsync(cancellationToken);

        NotifyChanged();
        return ToView(entity);
    }

    public async Task<int> ConnectAllAvailableAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.SkySoftLinkAccounts.AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var connectedCount = 0;
        foreach (var id in ids)
        {
            try
            {
                await ConnectAccountAsync(id, null, cancellationToken);
                connectedCount++;
            }
            catch
            {
                // Bỏ qua tài khoản chưa đủ thông tin khi bấm Kết nối nhanh tất cả.
            }
        }

        return connectedCount;
    }

    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in _liveSessions.Keys.ToList())
        {
            await DisconnectInternalAsync(key);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var connected = await db.SkySoftLinkAccounts.Where(x => x.IsConnected).ToListAsync(cancellationToken);
        var now = VietnamClock.Now;
        foreach (var acc in connected)
        {
            acc.IsConnected = false;
            acc.LastStatusMessage = $"Đã ngắt kết nối lúc {now:HH:mm:ss}";
            acc.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        NotifyChanged();
    }

    /// <summary>
    /// Gọi API từ TẤT CẢ các tài khoản đang Connect ("Login bao nhiêu tài khoản thì gọi API bấy nhiêu"),
    /// sau đó gộp và chuẩn hóa theo cặp khóa Biển số - Số hiệu để phát hiện & loại bỏ trùng lặp giữa các tỉnh.
    /// </summary>
    public async Task<SharedVehicleFetchSummary> FetchSharedVehiclesFromConnectedAsync(
        bool autoExcludeCrossProvince = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var connectedAccounts = await db.SkySoftLinkAccounts.AsNoTracking()
            .Where(x => x.IsConnected)
            .OrderBy(x => x.AreaCode)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (connectedAccounts.Count == 0)
        {
            throw new InvalidOperationException(
                "Chưa có tài khoản nào ở trạng thái Đang kết nối (Connected). Hãy bấm Kết nối ít nhất 1 tài khoản trước khi gọi dữ liệu chung.");
        }

        var rawVehiclesByAccount = new List<(SkySoftLinkAccount Account, IReadOnlyList<OnlineVehicleInfo> Vehicles)>();
        var accountLogs = new List<string>();
        var totalRawCount = 0;

        foreach (var account in connectedAccounts)
        {
            var token = Unprotect(account.ProtectedApiToken);
            if (string.IsNullOrWhiteSpace(account.ApiUserName) ||
                string.IsNullOrWhiteSpace(account.ApiKey) ||
                string.IsNullOrWhiteSpace(token))
            {
                accountLogs.Add($"{account.AreaCode} ({account.UserName}): Bỏ qua gọi GPS vì chưa cấu hình API Key/Token.");
                continue;
            }

            var vehicles = await CallOnlineTaxiApiAsync(account, token, cancellationToken);
            rawVehiclesByAccount.Add((account, vehicles));
            totalRawCount += vehicles.Count;
            accountLogs.Add($"{account.AreaCode} ({account.ApiUserName}): Lấy {vehicles.Count:N0} xe từ API.");
        }

        if (rawVehiclesByAccount.Count == 0)
        {
            throw new InvalidOperationException(
                "Các tài khoản đang kết nối chưa có cấu hình API (Username/Key/Token) để gọi danh sách xe.");
        }

        var existingMap = await db.SharedVehicleRegistries
            .ToDictionaryAsync(x => x.VehicleKeyComposite, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var knownAreaCodes = await db.SkySoftLinkAccounts.AsNoTracking()
            .Select(x => x.AreaCode.ToUpper())
            .Distinct()
            .ToListAsync(cancellationToken);

        var grouped = new Dictionary<string, List<(string AreaCode, OnlineVehicleInfo Vehicle)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (account, vehicles) in rawVehiclesByAccount)
        {
            foreach (var v in vehicles)
            {
                var plateNorm = VehicleKey.Normalize(v.PlateNo);
                var noNorm = VehicleKey.Normalize(v.VehicleNo);
                if (string.IsNullOrWhiteSpace(plateNorm) && string.IsNullOrWhiteSpace(noNorm))
                    continue;

                var compositeKey = $"{plateNorm}|{noNorm}";
                if (!grouped.TryGetValue(compositeKey, out var bucket))
                {
                    bucket = [];
                    grouped[compositeKey] = bucket;
                }
                bucket.Add((account.AreaCode.ToUpperInvariant(), v));
            }
        }

        var now = VietnamClock.Now;
        var crossProvinceDuplicatesRemoved = 0;

        foreach (var (compositeKey, entries) in grouped)
        {
            var distinctAreas = entries
                .Select(x => x.AreaCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (entries.Count > 1)
            {
                crossProvinceDuplicatesRemoved += entries.Count - 1;
            }

            // Chọn bản ghi có tín hiệu GPS / UpdateDate mới nhất làm đại diện.
            var bestEntry = entries
                .OrderByDescending(x => x.Vehicle.GpsDate ?? DateTime.MinValue)
                .ThenByDescending(x => x.Vehicle.UpdateDate ?? DateTime.MinValue)
                .First();

            var representative = bestEntry.Vehicle;
            var plateNorm = VehicleKey.Normalize(representative.PlateNo);
            var noNorm = VehicleKey.Normalize(representative.VehicleNo);

            var inferredArea = InferPrimaryArea(noNorm, distinctAreas, knownAreaCodes, bestEntry.AreaCode);

            if (!existingMap.TryGetValue(compositeKey, out var entity))
            {
                entity = new SharedVehicleRegistry
                {
                    VehicleKeyComposite = compositeKey,
                    PlateNo = representative.PlateNo.Trim(),
                    PlateNoNormalized = plateNorm,
                    VehicleNo = representative.VehicleNo.Trim(),
                    VehicleNoNormalized = noNorm,
                    VehicleId = representative.VehicleId,
                    VehicleCode = representative.VehicleCode,
                    PrimaryAreaCode = inferredArea,
                    SourceAreas = string.Join(", ", distinctAreas),
                    DuplicateCount = distinctAreas.Count,
                    GpsDate = representative.GpsDate,
                    UpdateDate = representative.UpdateDate,
                    IsExcluded = false,
                    ExcludeReason = string.Empty,
                    FetchedAt = now,
                    UpdatedAt = now
                };

                if (autoExcludeCrossProvince && string.IsNullOrWhiteSpace(representative.VehicleNo))
                {
                    entity.IsExcluded = true;
                    entity.ExcludeReason = "Thiếu Số hiệu xe";
                }

                db.SharedVehicleRegistries.Add(entity);
                existingMap[compositeKey] = entity;
            }
            else
            {
                entity.PlateNo = representative.PlateNo.Trim();
                entity.PlateNoNormalized = plateNorm;
                entity.VehicleNo = representative.VehicleNo.Trim();
                entity.VehicleNoNormalized = noNorm;
                entity.VehicleId = representative.VehicleId;
                entity.VehicleCode = representative.VehicleCode;
                if (string.IsNullOrWhiteSpace(entity.PrimaryAreaCode))
                    entity.PrimaryAreaCode = inferredArea;
                entity.SourceAreas = string.Join(", ", distinctAreas);
                entity.DuplicateCount = distinctAreas.Count;
                entity.GpsDate = representative.GpsDate;
                entity.UpdateDate = representative.UpdateDate;
                entity.FetchedAt = now;
                entity.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var allStored = await db.SharedVehicleRegistries.AsNoTracking().ToListAsync(cancellationToken);
        var excludedCount = allStored.Count(x => x.IsExcluded);
        var activeCount = allStored.Count - excludedCount;

        NotifyChanged();

        return new SharedVehicleFetchSummary(
            ConnectedAccountsCalled: rawVehiclesByAccount.Count,
            RawVehiclesFetched: totalRawCount,
            UniqueVehiclesStored: allStored.Count,
            CrossProvinceDuplicatesRemoved: crossProvinceDuplicatesRemoved,
            ExcludedVehiclesCount: excludedCount,
            ActiveVehiclesCount: activeCount,
            AccountLogs: accountLogs);
    }

    /// <summary>
    /// Tự động lọc và loại bỏ các bản ghi trùng lặp hoặc thông tin không cần thiết:
    /// - Gộp các dòng trùng Biển số nhưng lệch định dạng.
    /// - Đánh dấu loại bỏ các xe không có Số hiệu hoặc xe ngoại tỉnh không khớp các khu vực đang quản lý.
    /// </summary>
    public async Task<(int MergedDuplicates, int AutoExcluded)> DeduplicateAndCleanRegistryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var all = await db.SharedVehicleRegistries
            .OrderByDescending(x => x.GpsDate)
            .ThenByDescending(x => x.UpdateDate)
            .ToListAsync(cancellationToken);

        var connectedAreaCodes = await db.SkySoftLinkAccounts.AsNoTracking()
            .Where(x => x.IsConnected)
            .Select(x => x.AreaCode.ToUpper())
            .Distinct()
            .ToListAsync(cancellationToken);

        if (connectedAreaCodes.Count == 0)
        {
            connectedAreaCodes = await db.SkySoftLinkAccounts.AsNoTracking()
                .Select(x => x.AreaCode.ToUpper())
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        // 1. Gộp các bản ghi có cùng Biển số chuẩn hóa + Số hiệu chuẩn hóa (nếu có).
        var byKey = all.GroupBy(
            x => $"{VehicleKey.Normalize(x.PlateNo)}|{VehicleKey.Normalize(x.VehicleNo)}",
            StringComparer.OrdinalIgnoreCase).ToList();

        var mergedCount = 0;
        var autoExcluded = 0;
        var now = VietnamClock.Now;

        foreach (var group in byKey)
        {
            var items = group.ToList();
            var primary = items[0];

            if (items.Count > 1)
            {
                var mergedAreas = items
                    .SelectMany(x => x.SourceAreas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                primary.SourceAreas = string.Join(", ", mergedAreas);
                primary.DuplicateCount = Math.Max(primary.DuplicateCount, mergedAreas.Count);
                primary.UpdatedAt = now;

                for (var i = 1; i < items.Count; i++)
                {
                    db.SharedVehicleRegistries.Remove(items[i]);
                    mergedCount++;
                }
            }

            // 2. Chuẩn hóa khu vực sở hữu theo tiền tố Số hiệu (BL, VL, RG, CM, CT...) để 2 tỉnh không lấy nhầm xe của nhau.
            var matchedPrefix = connectedAreaCodes
                .FirstOrDefault(code => primary.VehicleNoNormalized.StartsWith(code, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchedPrefix))
            {
                primary.PrimaryAreaCode = matchedPrefix;
            }

            // 3. Loại bỏ thông tin không cần thiết: xe thiếu Biển số/Số hiệu hoặc không thuộc khu vực đang kết nối.
            if (string.IsNullOrWhiteSpace(primary.PlateNoNormalized) || string.IsNullOrWhiteSpace(primary.VehicleNoNormalized))
            {
                if (!primary.IsExcluded)
                {
                    primary.IsExcluded = true;
                    primary.ExcludeReason = "Thiếu Biển số hoặc Số hiệu";
                    primary.UpdatedAt = now;
                    autoExcluded++;
                }
            }
            else if (connectedAreaCodes.Count > 0 &&
                     !connectedAreaCodes.Contains(primary.PrimaryAreaCode, StringComparer.OrdinalIgnoreCase))
            {
                if (!primary.IsExcluded)
                {
                    primary.IsExcluded = true;
                    primary.ExcludeReason = $"Xe ngoài vùng kết nối ({primary.PrimaryAreaCode})";
                    primary.UpdatedAt = now;
                    autoExcluded++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        NotifyChanged();
        return (mergedCount, autoExcluded);
    }

    public async Task ToggleVehicleExcludeAsync(
        long id,
        bool isExcluded,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.SharedVehicleRegistries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy xe trong danh mục liên kết.");

        row.IsExcluded = isExcluded;
        row.ExcludeReason = isExcluded
            ? (string.IsNullOrWhiteSpace(reason) ? "Loại bỏ thủ công" : reason.Trim())
            : string.Empty;
        row.UpdatedAt = VietnamClock.Now;
        await db.SaveChangesAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task UpdateVehiclePrimaryAreaAsync(
        long id,
        string primaryAreaCode,
        CancellationToken cancellationToken = default)
    {
        var code = (primaryAreaCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Mã khu vực không hợp lệ.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.SharedVehicleRegistries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy xe trong danh mục liên kết.");

        row.PrimaryAreaCode = code;
        row.UpdatedAt = VietnamClock.Now;
        await db.SaveChangesAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task DeleteVehicleRegistryRowAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.SharedVehicleRegistries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is not null)
        {
            db.SharedVehicleRegistries.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            NotifyChanged();
        }
    }

    /// <summary>
    /// Đồng bộ các xe hợp lệ (đã lọc bỏ trùng & không bị đánh dấu Loại bỏ) sang bảng GPS Online Current
    /// của từng khu vực tương ứng.
    /// </summary>
    public async Task<int> PushCleanVehiclesToOnlineCurrentAsync(
        string? targetAreaCode = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.SharedVehicleRegistries.AsNoTracking()
            .Where(x => !x.IsExcluded);

        if (!string.IsNullOrWhiteSpace(targetAreaCode))
        {
            var code = targetAreaCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.PrimaryAreaCode == code);
        }

        var cleanVehicles = await query
            .OrderBy(x => x.PrimaryAreaCode)
            .ThenBy(x => x.VehicleNo)
            .ToListAsync(cancellationToken);

        if (cleanVehicles.Count == 0)
            throw new InvalidOperationException("Không có xe hợp lệ nào trong danh mục chung để đẩy sang GPS Online Current.");

        var byArea = cleanVehicles
            .Where(x => !string.IsNullOrWhiteSpace(x.PrimaryAreaCode))
            .GroupBy(x => x.PrimaryAreaCode.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var createdAt = VietnamClock.Now;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var totalPushed = 0;
        foreach (var group in byArea)
        {
            var areaCode = group.Key;
            await db.OnlineVehicleCurrents
                .Where(x => x.AreaCode == areaCode)
                .ExecuteDeleteAsync(cancellationToken);

            var entities = group.Select(x => new OnlineVehicleCurrent
            {
                AreaCode = areaCode,
                VehicleId = x.VehicleId,
                PlateNo = x.PlateNo,
                VehicleNo = x.VehicleNo,
                VehicleCode = x.VehicleCode,
                GpsDate = x.GpsDate,
                UpdateDate = x.UpdateDate,
                CreatedAt = createdAt
            }).ToList();

            db.OnlineVehicleCurrents.AddRange(entities);
            totalPushed += entities.Count;
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return totalPushed;
    }

    public async Task<SharedVehicleRegistryPage> GetSharedVehiclesPageAsync(
        int page = 1,
        int pageSize = 100,
        string? search = null,
        string filterMode = "all",
        string areaFilter = "",
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 20, 500);
        search = search?.Trim() ?? string.Empty;
        filterMode = string.IsNullOrWhiteSpace(filterMode) ? "all" : filterMode.Trim().ToLowerInvariant();
        areaFilter = areaFilter?.Trim().ToUpperInvariant() ?? string.Empty;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var baseQuery = db.SharedVehicleRegistries.AsNoTracking();

        var totalUnique = await baseQuery.CountAsync(cancellationToken);
        var multiProvinceCount = await baseQuery.CountAsync(x => x.DuplicateCount > 1, cancellationToken);
        var excludedCount = await baseQuery.CountAsync(x => x.IsExcluded, cancellationToken);
        var activeCleanCount = totalUnique - excludedCount;

        var filtered = baseQuery;

        if (!string.IsNullOrWhiteSpace(areaFilter))
        {
            filtered = filtered.Where(x => x.PrimaryAreaCode == areaFilter || EF.Functions.Like(x.SourceAreas, $"%{areaFilter}%"));
        }

        filtered = filterMode switch
        {
            "duplicates" => filtered.Where(x => x.DuplicateCount > 1),
            "excluded" => filtered.Where(x => x.IsExcluded),
            "active" => filtered.Where(x => !x.IsExcluded),
            _ => filtered
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            filtered = filtered.Where(x =>
                EF.Functions.Like(x.PlateNo, term) ||
                EF.Functions.Like(x.VehicleNo, term) ||
                EF.Functions.Like(x.VehicleCode, term) ||
                EF.Functions.Like(x.VehicleId, term) ||
                EF.Functions.Like(x.SourceAreas, term) ||
                EF.Functions.Like(x.PrimaryAreaCode, term) ||
                EF.Functions.Like(x.ExcludeReason, term));
        }

        var totalRows = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await filtered
            .OrderByDescending(x => x.DuplicateCount)
            .ThenBy(x => x.PrimaryAreaCode)
            .ThenBy(x => x.VehicleNo)
            .ThenBy(x => x.PlateNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SharedVehicleItemView(
                x.Id,
                x.PlateNo,
                x.VehicleNo,
                x.VehicleKeyComposite,
                x.VehicleId,
                x.VehicleCode,
                x.PrimaryAreaCode,
                x.SourceAreas,
                x.DuplicateCount,
                x.GpsDate,
                x.UpdateDate,
                x.IsExcluded,
                x.ExcludeReason,
                x.FetchedAt))
            .ToListAsync(cancellationToken);

        return new SharedVehicleRegistryPage(
            rows,
            totalRows,
            totalUnique,
            multiProvinceCount,
            excludedCount,
            activeCleanCount,
            page,
            pageSize,
            search,
            filterMode,
            areaFilter);
    }

    /// <summary>
    /// Cho phép SkySoftAppService lấy cấu hình API từ bảng Quản lý Liên kết (ưu tiên tài khoản đang Connect của khu vực).
    /// </summary>
    public async Task<(SkySoftAppAreaOptions? Options, string BaseUrl, int TimeoutSeconds, bool FilterDuplicates)> ResolveAreaApiAsync(
        string areaCode,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var code = (areaCode ?? string.Empty).Trim().ToUpperInvariant();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var account = await db.SkySoftLinkAccounts.AsNoTracking()
            .Where(x => x.AreaCode == code && !string.IsNullOrWhiteSpace(x.ApiUserName) && !string.IsNullOrWhiteSpace(x.ApiKey))
            .OrderByDescending(x => x.IsConnected)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
            return (null, _skySoftAppOptions.BaseUrl, _skySoftAppOptions.TimeoutSeconds, true);

        var token = Unprotect(account.ProtectedApiToken);
        if (string.IsNullOrWhiteSpace(token))
            return (null, account.ApiBaseUrl, account.TimeoutSeconds, account.FilterCrossProvinceDuplicates);

        var areaOptions = new SkySoftAppAreaOptions
        {
            Username = account.ApiUserName,
            Key = account.ApiKey,
            Token = token
        };

        return (areaOptions, account.ApiBaseUrl, account.TimeoutSeconds, account.FilterCrossProvinceDuplicates);
    }

    /// <summary>
    /// Lấy thông tin đăng nhập Authentication App (go.skysoft.vn) của khu vực từ Quản lý Liên kết
    /// để đồng bộ phiên làm việc khi không dùng form đăng nhập trên thanh điều hướng.
    /// </summary>
    public async Task<(string UserName, string Password)?> GetAreaAppCredentialsAsync(
        string areaCode,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var code = (areaCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.SkySoftLinkAccounts.AsNoTracking()
            .Where(x => x.AreaCode == code && !string.IsNullOrWhiteSpace(x.UserName) && !string.IsNullOrWhiteSpace(x.ProtectedPassword))
            .OrderByDescending(x => x.IsConnected)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
            return null;

        var password = Unprotect(account.ProtectedPassword);
        if (string.IsNullOrWhiteSpace(password))
            return null;

        return (account.UserName.Trim(), password);
    }

    /// <summary>
    /// Lọc danh sách xe theo quy tắc Biển số - Số hiệu đã cấu hình trong Quản lý Liên kết:
    /// - Loại bỏ trùng lặp (PlateNoNormalized | VehicleNoNormalized).
    /// - Loại bỏ các xe đã đánh dấu IsExcluded hoặc xe thuộc tỉnh khác khi bật FilterCrossProvinceDuplicates.
    /// </summary>
    public async Task<IReadOnlyList<OnlineVehicleInfo>> FilterAndDeduplicateVehiclesForAreaAsync(
        string areaCode,
        IReadOnlyList<OnlineVehicleInfo> rawVehicles,
        bool filterCrossProvince,
        CancellationToken cancellationToken = default)
    {
        if (rawVehicles.Count == 0) return rawVehicles;

        var code = (areaCode ?? string.Empty).Trim().ToUpperInvariant();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var registryRules = await db.SharedVehicleRegistries.AsNoTracking()
            .ToDictionaryAsync(x => x.VehicleKeyComposite, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OnlineVehicleInfo>(rawVehicles.Count);

        foreach (var vehicle in rawVehicles)
        {
            var plateNorm = VehicleKey.Normalize(vehicle.PlateNo);
            var noNorm = VehicleKey.Normalize(vehicle.VehicleNo);
            if (string.IsNullOrWhiteSpace(plateNorm) && string.IsNullOrWhiteSpace(noNorm))
                continue;

            var compositeKey = $"{plateNorm}|{noNorm}";
            if (!seenKeys.Add(compositeKey))
                continue;

            if (!string.IsNullOrWhiteSpace(plateNorm) && !seenPlates.Add(plateNorm))
                continue;

            if (registryRules.TryGetValue(compositeKey, out var rule))
            {
                if (rule.IsExcluded)
                    continue;

                if (filterCrossProvince &&
                    !string.IsNullOrWhiteSpace(rule.PrimaryAreaCode) &&
                    !string.Equals(rule.PrimaryAreaCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            result.Add(vehicle);
        }

        return result;
    }

    private async Task<LiveAppSession> LoginAppSessionAsync(
        SkySoftLinkAccount account,
        string password,
        CancellationToken cancellationToken)
    {
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        var baseUrl = string.IsNullOrWhiteSpace(account.AppBaseUrl)
            ? "https://go.skysoft.vn/"
            : account.AppBaseUrl.TrimEnd('/') + "/";

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", baseUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36");

        try
        {
            using (var initReq = new HttpRequestMessage(HttpMethod.Get, "/"))
            {
                initReq.Headers.Referrer = new Uri(baseUrl);
                using var _ = await client.SendAsync(initReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            var payload = new SkySoftLoginRequest
            {
                UserName = account.UserName.Trim(),
                Password = password,
                DeviceID = string.IsNullOrWhiteSpace(account.DeviceId) ? Guid.NewGuid().ToString("N") : account.DeviceId,
                DeviceName = account.DeviceName,
                DeviceModel = account.DeviceModel,
                DeviceBrand = account.DeviceBrand,
                AppOs = account.AppOs,
                OsVersion = account.OsVersion,
                AppVersion = account.AppVersion,
                FireBaseToken = string.Empty,
                Reconnect = false
            };

            using var loginReq = new HttpRequestMessage(HttpMethod.Post, "rest/win/v2/login")
            {
                Content = JsonContent.Create(payload)
            };
            loginReq.Headers.Referrer = new Uri(baseUrl);

            using var response = await client.SendAsync(loginReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Đăng nhập SkySoft ({account.UserName}) lỗi HTTP {(int)response.StatusCode}: {Limit(body, 300)}");
            }

            var hasToken = cookies.GetCookies(new Uri(baseUrl)).Cast<Cookie>()
                .Any(c => c.Name.Equals("tokenID", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));

            if (!hasToken)
            {
                throw new InvalidOperationException(
                    $"Tài khoản '{account.UserName}' không nhận được tokenID từ SkySoft. Kiểm tra lại tài khoản/mật khẩu.");
            }

            return new LiveAppSession(account.Id, account.UserName, account.AreaCode, cookies, handler, client);
        }
        catch
        {
            client.Dispose();
            handler.Dispose();
            throw;
        }
    }

    private static async Task<IReadOnlyList<OnlineVehicleInfo>> CallOnlineTaxiApiAsync(
        SkySoftLinkAccount account,
        string apiToken,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(account.TimeoutSeconds, 30, 600))
        };

        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var md5Bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{account.ApiUserName}-{unixTime}-{account.ApiKey}"));
        var md5 = Convert.ToHexString(md5Bytes).ToLowerInvariant();

        var baseUrl = string.IsNullOrWhiteSpace(account.ApiBaseUrl)
            ? "https://data.skysoft.vn/rest/api/"
            : account.ApiBaseUrl.TrimEnd('/') + "/";
        var url = new Uri(new Uri(baseUrl), "online_taxi");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation("time", unixTime.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("md5", md5);
        request.Headers.TryAddWithoutValidation("token", apiToken);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"API online_taxi ({account.AreaCode}) lỗi HTTP {(int)response.StatusCode}: {Limit(body, 300)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("vehicles", out var vehiclesEl) ||
            vehiclesEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<OnlineVehicleInfo>();
        foreach (var v in vehiclesEl.EnumerateArray())
        {
            list.Add(new OnlineVehicleInfo(
                GetJsonString(v, "vehicleID"),
                GetJsonString(v, "plateNo"),
                GetJsonString(v, "vehicleNo"),
                GetJsonString(v, "vehicleCode"),
                ParseApiDate(GetJsonString(v, "gpsDate")),
                ParseApiDate(GetJsonString(v, "updateDate"))));
        }

        return list;
    }

    private static string InferPrimaryArea(
        string vehicleNoNorm,
        IReadOnlyList<string> sourceAreas,
        IReadOnlyList<string> knownAreaCodes,
        string fallbackArea)
    {
        if (!string.IsNullOrWhiteSpace(vehicleNoNorm))
        {
            foreach (var code in knownAreaCodes.OrderByDescending(x => x.Length))
            {
                if (vehicleNoNorm.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                    return code.ToUpperInvariant();
            }
        }

        if (sourceAreas.Count == 1)
            return sourceAreas[0].ToUpperInvariant();

        return fallbackArea.ToUpperInvariant();
    }

    private static string GetJsonString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString();
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

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedOffset))
            return VietnamClock.FromUtc(parsedOffset.UtcDateTime);

        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.AllowWhiteSpaces, out var date))
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);

        return null;
    }

    private SkySoftLinkAccountView ToView(SkySoftLinkAccount x)
    {
        var hasLiveCookie = _liveSessions.TryGetValue(x.Id, out var session) && session.HasTokenCookie();
        return new SkySoftLinkAccountView(
            x.Id,
            x.AreaCode,
            x.AreaName,
            x.AppBaseUrl,
            x.UserName,
            !string.IsNullOrWhiteSpace(x.ProtectedPassword),
            x.DeviceId,
            x.DeviceName,
            x.DeviceBrand,
            x.DeviceModel,
            x.AppOs,
            x.OsVersion,
            x.AppVersion,
            x.ApiBaseUrl,
            x.ApiUserName,
            x.ApiKey,
            !string.IsNullOrWhiteSpace(x.ProtectedApiToken),
            x.TimeoutSeconds,
            x.MinOccupiedKm,
            x.MatchToleranceMinutes,
            x.Notes,
            x.FilterCrossProvinceDuplicates,
            x.IsConnected,
            hasLiveCookie,
            x.LastConnectedAt,
            x.LastStatusMessage,
            x.UpdatedAt);
    }

    private Task DisconnectInternalAsync(int id)
    {
        if (_liveSessions.TryRemove(id, out var session))
            session.Dispose();
        return Task.CompletedTask;
    }

    private string Protect(string plainText) =>
        string.IsNullOrWhiteSpace(plainText) ? string.Empty : _protector.Protect(plainText);

    private string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText)) return string.Empty;
        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Limit(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

    private void NotifyChanged() => Changed?.Invoke();

    public void Dispose()
    {
        foreach (var session in _liveSessions.Values)
            session.Dispose();
        _liveSessions.Clear();
        _initLock.Dispose();
    }

    private sealed class LiveAppSession(
        int accountId,
        string userName,
        string areaCode,
        CookieContainer cookies,
        HttpClientHandler handler,
        HttpClient client) : IDisposable
    {
        public int AccountId { get; } = accountId;
        public string UserName { get; } = userName;
        public string AreaCode { get; } = areaCode;

        public bool HasTokenCookie()
        {
            var uri = client.BaseAddress ?? new Uri("https://go.skysoft.vn/");
            return cookies.GetCookies(uri).Cast<Cookie>()
                .Any(c => c.Name.Equals("tokenID", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
        }

        public void Dispose()
        {
            client.Dispose();
            handler.Dispose();
        }
    }
}
