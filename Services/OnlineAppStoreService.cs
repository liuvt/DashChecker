using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class OnlineAppStoreService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public OnlineAppStoreService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ========================= XE ONLINE =========================

    public async Task<int> ReplaceOnlineVehicleCurrentAsync(
        AreaContext area,
        IReadOnlyList<OnlineVehicleInfo> rows,
        CancellationToken cancellationToken = default)
    {
        var createdAt = VietnamClock.Now;
        var dayStart = createdAt.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = rows
            .GroupBy(
                x => $"{(x.VehicleId ?? string.Empty).Trim()}|{(x.PlateNo ?? string.Empty).Trim()}|{(x.VehicleNo ?? string.Empty).Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.OnlineVehicleCurrents
            .Where(x => x.AreaCode == area.AreaCode && (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd || x.CreatedAt < dayEnd))
            .ExecuteDeleteAsync(cancellationToken);

        var entities = deduped.Select(x => new OnlineVehicleCurrent
        {
            AreaCode = area.AreaCode,
            VehicleId = x.VehicleId,
            PlateNo = x.PlateNo,
            VehicleNo = x.VehicleNo,
            VehicleCode = x.VehicleCode,
            GpsDate = x.GpsDate,
            UpdateDate = x.UpdateDate,
            CreatedAt = createdAt
        }).ToList();

        db.OnlineVehicleCurrents.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entities.Count;
    }

    public async Task<int> SaveOnlineVehicleCurrentToArchiveAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var current = await db.OnlineVehicleCurrents.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderBy(x => x.VehicleNo)
            .ToListAsync(cancellationToken);

        if (current.Count == 0)
            throw new InvalidOperationException("Current Xe online đang trống.");

        var createdAt = current.Max(x => x.CreatedAt);
        var dayStart = createdAt.Date;
        var dayEnd = dayStart.AddDays(1);
        var savedAt = VietnamClock.Now;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.OnlineVehicleArchives
            .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt >= dayStart && x.CreatedAt < dayEnd)
            .ExecuteDeleteAsync(cancellationToken);

        db.OnlineVehicleArchives.AddRange(current.Select(x => new OnlineVehicleArchive
        {
            AreaCode = x.AreaCode,
            VehicleId = x.VehicleId,
            PlateNo = x.PlateNo,
            VehicleNo = x.VehicleNo,
            VehicleCode = x.VehicleCode,
            GpsDate = x.GpsDate,
            UpdateDate = x.UpdateDate,
            CreatedAt = x.CreatedAt,
            SavedAt = savedAt
        }));

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return current.Count;
    }

    public async Task<OnlineVehicleSnapshot> GetOnlineVehicleCurrentAsync(
        AreaContext area,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        string? sortBy = null,
        bool sortDesc = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 20, 500);
        search = search?.Trim() ?? string.Empty;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var baseQuery = db.OnlineVehicleCurrents.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode);
        var createdAt = await baseQuery.MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);
        DateTime? savedAt = null;
        if (createdAt.HasValue)
        {
            savedAt = await db.OnlineVehicleArchives.AsNoTracking()
                .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt == createdAt.Value)
                .MaxAsync(x => (DateTime?)x.SavedAt, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            baseQuery = baseQuery.Where(x =>
                EF.Functions.Like(x.VehicleNo, term) ||
                EF.Functions.Like(x.PlateNo, term) ||
                EF.Functions.Like(x.VehicleCode, term) ||
                EF.Functions.Like(x.VehicleId, term));
        }

        var allRows = await baseQuery
            .Select(x => new OnlineVehicleStoredRow(
                new OnlineVehicleInfo(x.VehicleId, x.PlateNo, x.VehicleNo, x.VehicleCode, x.GpsDate, x.UpdateDate),
                x.CreatedAt, null))
            .ToListAsync(cancellationToken);

        var sorted = ApplyOnlineVehicleSort(allRows, sortBy, sortDesc);
        var total = sorted.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var pagedRows = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new OnlineVehicleSnapshot(area.AreaCode, createdAt, savedAt, pagedRows, total, page, pageSize, search);
    }

    public async Task<OnlineVehicleSnapshot> GetOnlineVehicleArchiveAsync(
        AreaContext area,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        string? sortBy = null,
        bool sortDesc = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 20, 500);
        search = search?.Trim() ?? string.Empty;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.OnlineVehicleArchives.AsNoTracking().Where(x => x.AreaCode == area.AreaCode);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.VehicleNo, term) || EF.Functions.Like(x.PlateNo, term) ||
                EF.Functions.Like(x.VehicleCode, term) || EF.Functions.Like(x.VehicleId, term));
        }

        var latestCreatedAt = await query.MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);
        var latestSavedAt = await query.MaxAsync(x => (DateTime?)x.SavedAt, cancellationToken);
        var allRows = await query
            .Select(x => new OnlineVehicleStoredRow(
                new OnlineVehicleInfo(x.VehicleId, x.PlateNo, x.VehicleNo, x.VehicleCode, x.GpsDate, x.UpdateDate),
                x.CreatedAt, x.SavedAt))
            .ToListAsync(cancellationToken);

        var sorted = ApplyOnlineVehicleSort(allRows, sortBy, sortDesc, isArchiveDefault: string.IsNullOrWhiteSpace(sortBy));
        var total = sorted.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var pagedRows = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new OnlineVehicleSnapshot(area.AreaCode, latestCreatedAt, latestSavedAt, pagedRows, total, page, pageSize, search);
    }

    public async Task<IReadOnlyList<OnlineVehicleStoredRow>> GetAllOnlineVehiclesForExportAsync(
        AreaContext area,
        string tab = "current",
        string? sortBy = null,
        bool sortDesc = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (!string.Equals(tab, "archive", StringComparison.OrdinalIgnoreCase))
        {
            var currentRows = await db.OnlineVehicleCurrents.AsNoTracking()
                .Where(x => x.AreaCode == area.AreaCode)
                .Select(x => new OnlineVehicleStoredRow(
                    new OnlineVehicleInfo(x.VehicleId, x.PlateNo, x.VehicleNo, x.VehicleCode, x.GpsDate, x.UpdateDate),
                    x.CreatedAt, null))
                .ToListAsync(cancellationToken);

            return ApplyOnlineVehicleSort(currentRows, sortBy, sortDesc);
        }

        var archiveRows = await db.OnlineVehicleArchives.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode)
            .Select(x => new OnlineVehicleStoredRow(
                new OnlineVehicleInfo(x.VehicleId, x.PlateNo, x.VehicleNo, x.VehicleCode, x.GpsDate, x.UpdateDate),
                x.CreatedAt, x.SavedAt))
            .ToListAsync(cancellationToken);

        return ApplyOnlineVehicleSort(archiveRows, sortBy, sortDesc, isArchiveDefault: string.IsNullOrWhiteSpace(sortBy));
    }

    private static List<OnlineVehicleStoredRow> ApplyOnlineVehicleSort(
        IReadOnlyList<OnlineVehicleStoredRow> rows,
        string? sortBy,
        bool sortDesc,
        bool isArchiveDefault = false)
    {
        if (rows.Count == 0)
            return [];

        if (isArchiveDefault && string.IsNullOrWhiteSpace(sortBy))
        {
            return rows
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Vehicle.VehicleNo, NaturalTextComparer.Instance)
                .ThenBy(x => x.Vehicle.PlateNo, NaturalTextComparer.Instance)
                .ToList();
        }

        var key = (sortBy ?? "VehicleNo").Trim();
        IOrderedEnumerable<OnlineVehicleStoredRow> ordered = key switch
        {
            "PlateNo" => sortDesc
                ? rows.OrderByDescending(x => x.Vehicle.PlateNo, NaturalTextComparer.Instance)
                : rows.OrderBy(x => x.Vehicle.PlateNo, NaturalTextComparer.Instance),
            "VehicleCode" => sortDesc
                ? rows.OrderByDescending(x => x.Vehicle.VehicleCode, NaturalTextComparer.Instance)
                : rows.OrderBy(x => x.Vehicle.VehicleCode, NaturalTextComparer.Instance),
            "VehicleId" => sortDesc
                ? rows.OrderByDescending(x => x.Vehicle.VehicleId, NaturalTextComparer.Instance)
                : rows.OrderBy(x => x.Vehicle.VehicleId, NaturalTextComparer.Instance),
            "Signal" => sortDesc
                ? rows.OrderByDescending(x => x.HasActiveGpsSignal ? 1 : 0).ThenByDescending(x => x.Vehicle.GpsDate)
                : rows.OrderBy(x => x.HasActiveGpsSignal ? 1 : 0).ThenBy(x => x.Vehicle.GpsDate),
            "GpsDate" => sortDesc
                ? rows.OrderByDescending(x => x.Vehicle.GpsDate.HasValue ? 1 : 0).ThenByDescending(x => x.Vehicle.GpsDate)
                : rows.OrderBy(x => x.Vehicle.GpsDate.HasValue ? 0 : 1).ThenBy(x => x.Vehicle.GpsDate),
            "UpdateDate" => sortDesc
                ? rows.OrderByDescending(x => x.Vehicle.UpdateDate.HasValue ? 1 : 0).ThenByDescending(x => x.Vehicle.UpdateDate)
                : rows.OrderBy(x => x.Vehicle.UpdateDate.HasValue ? 0 : 1).ThenBy(x => x.Vehicle.UpdateDate),
            "SavedAt" => sortDesc
                ? rows.OrderByDescending(x => x.SavedAt.HasValue ? 1 : 0).ThenByDescending(x => x.SavedAt)
                : rows.OrderBy(x => x.SavedAt.HasValue ? 0 : 1).ThenBy(x => x.SavedAt),
            "CreatedAt" => sortDesc
                ? rows.OrderByDescending(x => x.CreatedAt)
                : rows.OrderBy(x => x.CreatedAt),
            _ => sortDesc
                ? rows.OrderByDescending(x => x.Vehicle.VehicleNo, NaturalTextComparer.Instance)
                : rows.OrderBy(x => x.Vehicle.VehicleNo, NaturalTextComparer.Instance)
        };

        return ordered
            .ThenBy(x => x.Vehicle.VehicleNo, NaturalTextComparer.Instance)
            .ThenBy(x => x.Vehicle.PlateNo, NaturalTextComparer.Instance)
            .ToList();
    }

    private sealed class NaturalTextComparer : IComparer<string?>
    {
        public static NaturalTextComparer Instance { get; } = new();

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

    // ========================= BÁO CÁO CUỐC APP =========================

    public async Task<int> ReplaceAppTripCurrentAsync(
        AreaContext area,
        OnlineTripResult result,
        CancellationToken cancellationToken = default)
    {
        var createdAt = VietnamClock.Now;
        var dayStart = createdAt.Date;
        var dayEnd = dayStart.AddDays(1);

        var deduped = result.Trips
            .GroupBy(
                x => $"{(x.GeneratedId ?? string.Empty).Trim()}|{(x.TripId ?? string.Empty).Trim()}|{(x.VehicleNo ?? string.Empty).Trim()}|{x.PickupDate:yyyyMMddHHmmss}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.AppTripCurrents
            .Where(x => x.AreaCode == area.AreaCode && (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd || x.CreatedAt < dayEnd))
            .ExecuteDeleteAsync(cancellationToken);

        var entities = deduped.Select((x, index) => new AppTripCurrent
        {
            AreaCode = area.AreaCode,
            RowOrder = index + 1,
            GeneratedId = x.GeneratedId,
            VehicleNo = x.VehicleNo,
            PlateNo = x.PlateNo,
            UserName = x.UserName,
            PickupDate = x.PickupDate,
            DropOffDate = x.DropOffDate,
            Km = RoundKm(x.Km),
            EmptyKm = RoundKm(x.EmptyKm),
            TotalKm = RoundKm(x.TotalKm),
            WaitTimeMinutes = x.WaitTimeMinutes,
            WaitCharge = x.WaitCharge,
            Charge = x.Charge,
            RealCharge = x.RealCharge,
            TripId = x.TripId,
            FromPlaceName = x.FromPlaceName,
            ToPlaceName = x.ToPlaceName,
            DatePart = x.DatePart,
            TimePart = x.TimePart,
            VehicleDateKey = x.VehicleDateKey,
            VehicleCode = x.VehicleCode,
            CreatedAt = createdAt
        }).ToList();

        db.AppTripCurrents.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entities.Count;
    }

    public async Task<int> SaveAppTripCurrentToArchiveAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var current = await db.AppTripCurrents.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderBy(x => x.RowOrder)
            .ToListAsync(cancellationToken);
        if (current.Count == 0)
            throw new InvalidOperationException("Current Báo cáo cuốc khách đang trống.");

        var createdAt = current.Max(x => x.CreatedAt);
        var dayStart = createdAt.Date;
        var dayEnd = dayStart.AddDays(1);
        var savedAt = VietnamClock.Now;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AppTripArchives
            .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt >= dayStart && x.CreatedAt < dayEnd)
            .ExecuteDeleteAsync(cancellationToken);

        db.AppTripArchives.AddRange(current.Select(x => new AppTripArchive
        {
            AreaCode = x.AreaCode,
            RowOrder = x.RowOrder,
            GeneratedId = x.GeneratedId,
            VehicleNo = x.VehicleNo,
            PlateNo = x.PlateNo,
            UserName = x.UserName,
            PickupDate = x.PickupDate,
            DropOffDate = x.DropOffDate,
            Km = RoundKm(x.Km),
            EmptyKm = RoundKm(x.EmptyKm),
            TotalKm = RoundKm(x.TotalKm),
            WaitTimeMinutes = x.WaitTimeMinutes,
            WaitCharge = x.WaitCharge,
            Charge = x.Charge,
            RealCharge = x.RealCharge,
            TripId = x.TripId,
            FromPlaceName = x.FromPlaceName,
            ToPlaceName = x.ToPlaceName,
            DatePart = x.DatePart,
            TimePart = x.TimePart,
            VehicleDateKey = x.VehicleDateKey,
            VehicleCode = x.VehicleCode,
            CreatedAt = x.CreatedAt,
            SavedAt = savedAt
        }));

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return current.Count;
    }

    public async Task<AppTripSnapshot> GetAppTripCurrentAsync(
        AreaContext area,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 20, 500);
        search = search?.Trim() ?? string.Empty;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.AppTripCurrents.AsNoTracking().Where(x => x.AreaCode == area.AreaCode);
        var createdAt = await query.MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);
        DateTime? savedAt = null;
        if (createdAt.HasValue)
            savedAt = await db.AppTripArchives.AsNoTracking()
                .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt == createdAt.Value)
                .MaxAsync(x => (DateTime?)x.SavedAt, cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.VehicleNo, term) || EF.Functions.Like(x.PlateNo, term) ||
                EF.Functions.Like(x.UserName, term) || EF.Functions.Like(x.TripId, term) ||
                EF.Functions.Like(x.FromPlaceName, term) || EF.Functions.Like(x.ToPlaceName, term));
        }

        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var rows = await query.OrderBy(x => x.RowOrder)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AppTripStoredRow(
                new OnlineTripInfo(x.GeneratedId, x.VehicleNo, x.PlateNo, x.UserName, x.PickupDate,
                    x.DropOffDate, x.Km, x.EmptyKm, x.TotalKm, x.WaitTimeMinutes, x.WaitCharge, x.Charge,
                    x.RealCharge, x.TripId, x.FromPlaceName, x.ToPlaceName, x.VehicleCode),
                x.CreatedAt, null))
            .ToListAsync(cancellationToken);

        var fromAt = VietnamClock.Now.Date.AddDays(-1).AddHours(5);
        var toAt = VietnamClock.Now.Date.AddHours(5);
        return new AppTripSnapshot(area.AreaCode, createdAt, savedAt, fromAt, toAt, rows, total, page, pageSize, search);
    }

    public async Task<AppTripSnapshot> GetAppTripArchiveAsync(
        AreaContext area,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 20, 500);
        search = search?.Trim() ?? string.Empty;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.AppTripArchives.AsNoTracking().Where(x => x.AreaCode == area.AreaCode);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.VehicleNo, term) || EF.Functions.Like(x.PlateNo, term) ||
                EF.Functions.Like(x.UserName, term) || EF.Functions.Like(x.TripId, term) ||
                EF.Functions.Like(x.FromPlaceName, term) || EF.Functions.Like(x.ToPlaceName, term));
        }

        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var latestCreatedAt = await query.MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);
        var latestSavedAt = await query.MaxAsync(x => (DateTime?)x.SavedAt, cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.RowOrder)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AppTripStoredRow(
                new OnlineTripInfo(x.GeneratedId, x.VehicleNo, x.PlateNo, x.UserName, x.PickupDate,
                    x.DropOffDate, x.Km, x.EmptyKm, x.TotalKm, x.WaitTimeMinutes, x.WaitCharge, x.Charge,
                    x.RealCharge, x.TripId, x.FromPlaceName, x.ToPlaceName, x.VehicleCode),
                x.CreatedAt, x.SavedAt))
            .ToListAsync(cancellationToken);

        var fromAt = (latestCreatedAt ?? VietnamClock.Now).Date.AddDays(-1).AddHours(5);
        var toAt = (latestCreatedAt ?? VietnamClock.Now).Date.AddHours(5);
        return new AppTripSnapshot(area.AreaCode, latestCreatedAt, latestSavedAt, fromAt, toAt, rows, total, page, pageSize, search);
    }

    private static decimal RoundKm(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
