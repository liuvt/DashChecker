using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class TaxiTripStoreService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public TaxiTripStoreService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Cập nhật chỉ làm việc với CURRENT của đúng khu vực và đúng ngày CreatedAt hiện hành.
    /// Không đụng vào Kho lưu trữ.
    /// </summary>
    public async Task ReplaceCurrentAsync(
        AreaContext area,
        ParsedTaxiReport report,
        CancellationToken cancellationToken = default)
    {
        var createdAt = VietnamClock.Now;
        var startOfDay = createdAt.Date;
        var startOfNextDay = startOfDay.AddDays(1);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.TaxiTripCurrents
            .Where(x => x.AreaCode == area.AreaCode &&
                        x.CreatedAt >= startOfDay && x.CreatedAt < startOfNextDay)
            .ExecuteDeleteAsync(cancellationToken);

        await db.TaxiTripCurrentSyncs
            .Where(x => x.AreaCode == area.AreaCode &&
                        x.CreatedAt >= startOfDay && x.CreatedAt < startOfNextDay)
            .ExecuteDeleteAsync(cancellationToken);

        var sync = new TaxiTripCurrentSync
        {
            AreaCode = area.AreaCode,
            AreaName = area.AreaName,
            SourceUserName = area.UserName,
            FileId = report.FileId,
            ReportId = 32,
            FromDate = report.FromDate.Date,
            ToDate = report.ToDate.Date,
            FromTime = report.FromTime,
            ToTime = report.ToTime,
            RowCount = report.Trips.Count,
            CreatedAt = createdAt,
            SavedAt = null
        };

        db.TaxiTripCurrentSyncs.Add(sync);
        await db.SaveChangesAsync(cancellationToken);

        var rows = report.Trips.Select((trip, index) => new TaxiTripCurrent
        {
            SyncId = sync.Id,
            AreaCode = area.AreaCode,
            RowOrder = index + 1,
            SoHieu = trip.SoHieu,
            BienSo = trip.BienSo,
            BatDau = trip.BatDau,
            KetThuc = trip.KetThuc,
            KmCoKhach = trip.KmCoKhach,
            KmRong = trip.KmRong,
            TongKm = trip.TongKm,
            ThanhTien = trip.ThanhTien,
            DiemDau = trip.DiemDau,
            DiemCuoi = trip.DiemCuoi,
            DriverNames = string.Empty,
            DriverEmployeeCodes = string.Empty,
            DriverPhones = string.Empty,
            ShiftSoTai = string.Empty,
            DriverCount = 0,
            DriverMatchStatus = "Chưa ghép ca",
            DriverShiftStartAt = null,
            DriverShiftNextAt = null,
            AppUserName = string.Empty,
            AppVehicleNo = string.Empty,
            AppVehicleCode = string.Empty,
            AppTripId = string.Empty,
            AppMatchStatus = "Chưa ghép Online App",
            CreatedAt = createdAt
        }).ToList();

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.TaxiTripCurrents.AddRange(rows);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Chỉ khi người dùng bấm Lưu mới copy CURRENT hôm nay sang Kho.
    /// Cùng khu vực + cùng kỳ báo cáo được replace để không phát sinh bản ghi trùng.
    /// </summary>
    public async Task<int> SaveCurrentToArchiveAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var startOfDay = now.Date;
        var startOfNextDay = startOfDay.AddDays(1);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var currentSync = await db.TaxiTripCurrentSyncs
            .AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode &&
                        x.CreatedAt >= startOfDay && x.CreatedAt < startOfNextDay)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentSync is null)
            throw new InvalidOperationException($"{area.AreaName} chưa có dữ liệu Current hôm nay để lưu.");

        var currentRows = await db.TaxiTripCurrents
            .AsNoTracking()
            .Where(x => x.SyncId == currentSync.Id)
            .OrderBy(x => x.RowOrder)
            .ToListAsync(cancellationToken);

        if (currentRows.Count == 0)
            throw new InvalidOperationException("Dữ liệu Current đang rỗng, không thể lưu kho.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Kho là bản chốt theo ngày CreatedAt. Lưu lại trong cùng ngày sẽ replace toàn bộ bản chốt ngày đó.
        var archiveDay = currentSync.CreatedAt.Date;
        var archiveNextDay = archiveDay.AddDays(1);
        var oldArchiveSyncIds = await db.TaxiTripArchiveSyncs
            .Where(x => x.AreaCode == area.AreaCode &&
                        x.CreatedAt >= archiveDay && x.CreatedAt < archiveNextDay)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (oldArchiveSyncIds.Count > 0)
        {
            await db.TaxiTripArchives
                .Where(x => oldArchiveSyncIds.Contains(x.SyncId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.TaxiTripArchiveSyncs
                .Where(x => oldArchiveSyncIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var archiveSync = new TaxiTripArchiveSync
        {
            AreaCode = currentSync.AreaCode,
            AreaName = currentSync.AreaName,
            SourceUserName = currentSync.SourceUserName,
            FileId = currentSync.FileId,
            ReportId = currentSync.ReportId,
            FromDate = currentSync.FromDate,
            ToDate = currentSync.ToDate,
            FromTime = currentSync.FromTime,
            ToTime = currentSync.ToTime,
            RowCount = currentRows.Count,
            CreatedAt = currentSync.CreatedAt,
            SavedAt = now
        };

        db.TaxiTripArchiveSyncs.Add(archiveSync);
        await db.SaveChangesAsync(cancellationToken);

        var archiveRows = currentRows.Select(x => new TaxiTripArchive
        {
            SyncId = archiveSync.Id,
            AreaCode = area.AreaCode,
            RowOrder = x.RowOrder,
            SoHieu = x.SoHieu,
            BienSo = x.BienSo,
            BatDau = x.BatDau,
            KetThuc = x.KetThuc,
            KmCoKhach = x.KmCoKhach,
            KmRong = x.KmRong,
            TongKm = x.TongKm,
            ThanhTien = x.ThanhTien,
            DiemDau = x.DiemDau,
            DiemCuoi = x.DiemCuoi,
            DriverNames = x.DriverNames,
            DriverEmployeeCodes = x.DriverEmployeeCodes,
            DriverPhones = x.DriverPhones,
            ShiftSoTai = x.ShiftSoTai,
            DriverCount = x.DriverCount,
            DriverMatchStatus = x.DriverMatchStatus,
            DriverShiftStartAt = x.DriverShiftStartAt,
            DriverShiftNextAt = x.DriverShiftNextAt,
            AppUserName = x.AppUserName,
            AppVehicleNo = x.AppVehicleNo,
            AppVehicleCode = x.AppVehicleCode,
            AppTripId = x.AppTripId,
            AppMatchStatus = x.AppMatchStatus,
            CreatedAt = currentSync.CreatedAt,
            SavedAt = now
        }).ToList();

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.TaxiTripArchives.AddRange(archiveRows);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken);

        var trackedCurrent = await db.TaxiTripCurrentSyncs
            .FirstAsync(x => x.Id == currentSync.Id, cancellationToken);
        trackedCurrent.SavedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return archiveRows.Count;
    }

    public async Task<OnlineAppApplyResult> ApplyOnlineAppInfoAsync(
        AreaContext area,
        IReadOnlyList<OnlineTripInfo> onlineTrips,
        int toleranceMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var today = VietnamClock.Now.Date;
        var tomorrow = today.AddDays(1);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var sync = await db.TaxiTripCurrentSyncs
            .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt >= today && x.CreatedAt < tomorrow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (sync is null)
            return new OnlineAppApplyResult(0, 0);

        var currentRows = await db.TaxiTripCurrents
            .Where(x => x.SyncId == sync.Id && x.AreaCode == area.AreaCode)
            .OrderBy(x => x.BatDau)
            .ToListAsync(cancellationToken);

        foreach (var row in currentRows)
        {
            row.AppUserName = string.Empty;
            row.AppVehicleNo = string.Empty;
            row.AppVehicleCode = string.Empty;
            row.AppTripId = string.Empty;
            row.AppMatchStatus = "Không tìm thấy cuốc Online App";
        }

        var tolerance = TimeSpan.FromMinutes(Math.Clamp(toleranceMinutes, 1, 120));
        var available = onlineTrips.Select((trip, index) => new { trip, index }).ToList();
        var used = new HashSet<int>();
        var matched = 0;

        foreach (var row in currentRows)
        {
            var plate = VehicleKey.Normalize(row.BienSo);
            var soHieu = VehicleKey.Normalize(row.SoHieu);
            var candidate = available
                .Where(x => !used.Contains(x.index) && VehicleKey.Normalize(x.trip.PlateNo) == plate)
                .Select(x => new
                {
                    x.trip,
                    x.index,
                    VehicleRank = !string.IsNullOrWhiteSpace(x.trip.VehicleNo) && VehicleKey.Normalize(x.trip.VehicleNo) == soHieu ? 0 : 1,
                    Difference = (x.trip.PickupDate - row.BatDau).Duration()
                })
                .Where(x => x.Difference <= tolerance)
                .OrderBy(x => x.VehicleRank)
                .ThenBy(x => x.Difference)
                .FirstOrDefault();

            if (candidate is null)
                continue;

            used.Add(candidate.index);
            row.AppUserName = candidate.trip.UserName;
            row.AppVehicleNo = candidate.trip.VehicleNo;
            row.AppVehicleCode = candidate.trip.VehicleCode;
            row.AppTripId = candidate.trip.TripId;
            row.AppMatchStatus = $"Khớp Online App ±{candidate.Difference.TotalSeconds:N0}s";
            matched++;
        }

        if (matched > 0)
            sync.SavedAt = null; // Current thay đổi sau khi ghép App, cần chốt Kho lại.

        await db.SaveChangesAsync(cancellationToken);
        return new OnlineAppApplyResult(currentRows.Count, matched);
    }

    public async Task<long> AddCurrentRowAsync(
        AreaContext area,
        TaxiTripCurrentEditModel input,
        CancellationToken cancellationToken = default)
    {
        ValidateCurrentInput(input);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var today = VietnamClock.Now.Date;
        var tomorrow = today.AddDays(1);
        var sync = await db.TaxiTripCurrentSyncs
            .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt >= today && x.CreatedAt < tomorrow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sync is null)
        {
            var now = VietnamClock.Now;
            sync = new TaxiTripCurrentSync
            {
                AreaCode = area.AreaCode,
                AreaName = area.AreaName,
                SourceUserName = area.UserName,
                FileId = "manual",
                ReportId = 32,
                FromDate = now.Date.AddDays(-1),
                ToDate = now.Date,
                FromTime = new TimeSpan(5, 0, 0),
                ToTime = new TimeSpan(5, 0, 0),
                RowCount = 0,
                CreatedAt = now,
                SavedAt = null
            };
            db.TaxiTripCurrentSyncs.Add(sync);
            await db.SaveChangesAsync(cancellationToken);
        }

        var rowOrder = (await db.TaxiTripCurrents
            .Where(x => x.SyncId == sync.Id)
            .Select(x => (int?)x.RowOrder)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var entity = CreateManualCurrentEntity(sync, area, rowOrder, input);
        db.TaxiTripCurrents.Add(entity);
        sync.RowCount = await db.TaxiTripCurrents.CountAsync(x => x.SyncId == sync.Id, cancellationToken) + 1;
        sync.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateCurrentRowAsync(
        AreaContext area,
        long id,
        TaxiTripCurrentEditModel input,
        CancellationToken cancellationToken = default)
    {
        ValidateCurrentInput(input);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TaxiTripCurrents
            .FirstOrDefaultAsync(x => x.Id == id && x.AreaCode == area.AreaCode, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy cuốc Current cần sửa.");

        var sync = await db.TaxiTripCurrentSyncs
            .FirstOrDefaultAsync(x => x.Id == entity.SyncId && x.AreaCode == area.AreaCode, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy snapshot Current của cuốc xe.");

        var matchingKeysChanged = !string.Equals(entity.SoHieu, input.SoHieu.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(entity.BienSo, input.BienSo.Trim(), StringComparison.OrdinalIgnoreCase)
            || entity.BatDau != input.BatDau;
        ApplyManualCurrentInput(entity, input, matchingKeysChanged);
        sync.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCurrentRowAsync(
        AreaContext area,
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = await db.TaxiTripCurrents
            .FirstOrDefaultAsync(x => x.Id == id && x.AreaCode == area.AreaCode, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy cuốc Current cần xóa.");

        var sync = await db.TaxiTripCurrentSyncs
            .FirstAsync(x => x.Id == entity.SyncId && x.AreaCode == area.AreaCode, cancellationToken);

        db.TaxiTripCurrents.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        sync.RowCount = await db.TaxiTripCurrents.CountAsync(x => x.SyncId == sync.Id, cancellationToken);
        sync.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static TaxiTripCurrent CreateManualCurrentEntity(
        TaxiTripCurrentSync sync,
        AreaContext area,
        int rowOrder,
        TaxiTripCurrentEditModel input)
    {
        var entity = new TaxiTripCurrent
        {
            SyncId = sync.Id,
            AreaCode = area.AreaCode,
            RowOrder = rowOrder,
            DriverMatchStatus = "Chưa ghép ca",
            AppMatchStatus = "Chưa ghép Online App",
            CreatedAt = sync.CreatedAt
        };
        ApplyManualCurrentInput(entity, input, true);
        return entity;
    }

    private static void ApplyManualCurrentInput(TaxiTripCurrent entity, TaxiTripCurrentEditModel input, bool resetMatching)
    {
        entity.SoHieu = input.SoHieu.Trim();
        entity.BienSo = input.BienSo.Trim();
        entity.BatDau = input.BatDau;
        entity.KetThuc = input.KetThuc;
        entity.KmCoKhach = input.KmCoKhach;
        entity.KmRong = input.KmRong;
        entity.TongKm = input.TongKm;
        entity.ThanhTien = input.ThanhTien;
        entity.DiemDau = input.DiemDau.Trim();
        entity.DiemCuoi = input.DiemCuoi.Trim();

        if (resetMatching)
        {
            // Chỉ xóa metadata ghép khi người dùng đổi khóa dùng để đối chiếu cuốc.
            entity.DriverNames = string.Empty;
            entity.DriverEmployeeCodes = string.Empty;
            entity.DriverPhones = string.Empty;
            entity.ShiftSoTai = string.Empty;
            entity.DriverCount = 0;
            entity.DriverMatchStatus = "Chưa ghép lại sau chỉnh sửa";
            entity.DriverShiftStartAt = null;
            entity.DriverShiftNextAt = null;
            entity.AppUserName = string.Empty;
            entity.AppVehicleNo = string.Empty;
            entity.AppVehicleCode = string.Empty;
            entity.AppTripId = string.Empty;
            entity.AppMatchStatus = "Chưa ghép lại sau chỉnh sửa";
        }
    }

    private static void ValidateCurrentInput(TaxiTripCurrentEditModel input)
    {
        if (string.IsNullOrWhiteSpace(input.SoHieu))
            throw new InvalidOperationException("Số hiệu không được để trống.");
        if (string.IsNullOrWhiteSpace(input.BienSo))
            throw new InvalidOperationException("Biển số không được để trống.");
        if (input.KetThuc < input.BatDau)
            throw new InvalidOperationException("Thời gian kết thúc phải lớn hơn hoặc bằng thời gian bắt đầu.");
        if (input.KmCoKhach < 0 || input.KmRong < 0 || input.TongKm < 0 || input.ThanhTien < 0)
            throw new InvalidOperationException("KM và Thành tiền không được là số âm.");
    }

    /// <summary>
    /// Lấy toàn bộ Current của khu vực hiện tại, không phụ thuộc paging/search trên UI.
    /// Dùng cho thao tác xuất toàn bộ dữ liệu sang Google Sheet.
    /// </summary>
    public async Task<IReadOnlyList<TaxiTripListItem>> GetAllCurrentRowsAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        var today = VietnamClock.Now.Date;
        var tomorrow = today.AddDays(1);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var sync = await db.TaxiTripCurrentSyncs.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt >= today && x.CreatedAt < tomorrow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sync is null)
            return Array.Empty<TaxiTripListItem>();

        return await db.TaxiTripCurrents.AsNoTracking()
            .Where(x => x.SyncId == sync.Id && x.AreaCode == area.AreaCode)
            .OrderBy(x => x.SoHieu)
            .ThenBy(x => x.BatDau)
            .Select(x => new TaxiTripListItem(
                x.Id, x.RowOrder, x.SoHieu, x.BienSo, x.BatDau, x.KetThuc,
                x.KmCoKhach, x.KmRong, x.TongKm, x.ThanhTien, x.DiemDau, x.DiemCuoi,
                x.DriverNames, x.DriverEmployeeCodes, x.DriverPhones, x.ShiftSoTai,
                x.DriverCount, x.DriverMatchStatus, x.DriverShiftStartAt, x.DriverShiftNextAt,
                x.AppUserName, x.AppVehicleNo, x.AppVehicleCode, x.AppTripId, x.AppMatchStatus))
            .ToListAsync(cancellationToken);
    }

    public async Task<CurrentTaxiTripPage?> GetCurrentPageAsync(
        AreaContext area,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 20, 500);
        page = Math.Max(1, page);
        search = search?.Trim() ?? string.Empty;

        var today = VietnamClock.Now.Date;
        var tomorrow = today.AddDays(1);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var sync = await db.TaxiTripCurrentSyncs.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode && x.CreatedAt >= today && x.CreatedAt < tomorrow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sync is null)
            return null;

        var allTripsQuery = db.TaxiTripCurrents.AsNoTracking()
            .Where(x => x.SyncId == sync.Id && x.AreaCode == area.AreaCode);

        var summaryData = await allTripsQuery
            .Select(x => new { x.BienSo, x.KmCoKhach, x.KmRong, x.TongKm, x.ThanhTien })
            .ToListAsync(cancellationToken);

        var summary = new TaxiTripSummary(
            summaryData.Count,
            summaryData.Select(x => x.BienSo).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            summaryData.Sum(x => x.ThanhTien),
            summaryData.Sum(x => x.KmCoKhach),
            summaryData.Sum(x => x.KmRong),
            summaryData.Sum(x => x.TongKm));

        IQueryable<TaxiTripCurrent> filtered = allTripsQuery;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            filtered = filtered.Where(x =>
                EF.Functions.Like(x.SoHieu, term) ||
                EF.Functions.Like(x.BienSo, term) ||
                EF.Functions.Like(x.DiemDau, term) ||
                EF.Functions.Like(x.DiemCuoi, term) ||
                EF.Functions.Like(x.DriverNames, term) ||
                EF.Functions.Like(x.DriverEmployeeCodes, term) ||
                EF.Functions.Like(x.ShiftSoTai, term) ||
                EF.Functions.Like(x.AppUserName, term) ||
                EF.Functions.Like(x.AppVehicleNo, term) ||
                EF.Functions.Like(x.AppTripId, term));
        }

        var totalRows = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await filtered
            .OrderBy(x => x.SoHieu)
            .ThenBy(x => x.BatDau)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new TaxiTripListItem(
                x.Id, x.RowOrder, x.SoHieu, x.BienSo, x.BatDau, x.KetThuc,
                x.KmCoKhach, x.KmRong, x.TongKm, x.ThanhTien, x.DiemDau, x.DiemCuoi,
                x.DriverNames, x.DriverEmployeeCodes, x.DriverPhones, x.ShiftSoTai,
                x.DriverCount, x.DriverMatchStatus, x.DriverShiftStartAt, x.DriverShiftNextAt,
                x.AppUserName, x.AppVehicleNo, x.AppVehicleCode, x.AppTripId, x.AppMatchStatus))
            .ToListAsync(cancellationToken);

        return new CurrentTaxiTripPage(
            area.AreaCode, area.AreaName, sync.Id, sync.FileId,
            sync.FromDate, sync.ToDate, sync.FromTime, sync.ToTime,
            sync.CreatedAt, sync.SavedAt, rows, summary,
            totalRows, page, pageSize, search);
    }

    public async Task<ArchiveTaxiTripPage> GetArchivePageAsync(
        AreaContext area,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 20, 500);
        page = Math.Max(1, page);
        search = search?.Trim() ?? string.Empty;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<TaxiTripArchive> query = db.TaxiTripArchives.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.SoHieu, term) ||
                EF.Functions.Like(x.BienSo, term) ||
                EF.Functions.Like(x.DiemDau, term) ||
                EF.Functions.Like(x.DiemCuoi, term) ||
                EF.Functions.Like(x.DriverNames, term) ||
                EF.Functions.Like(x.DriverEmployeeCodes, term) ||
                EF.Functions.Like(x.ShiftSoTai, term) ||
                EF.Functions.Like(x.AppUserName, term) ||
                EF.Functions.Like(x.AppVehicleNo, term) ||
                EF.Functions.Like(x.AppTripId, term));
        }

        var totalRows = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.SoHieu)
            .ThenBy(x => x.BatDau)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new TaxiTripArchiveListItem(
                x.Id, x.CreatedAt, x.SavedAt, x.RowOrder, x.SoHieu, x.BienSo,
                x.BatDau, x.KetThuc, x.KmCoKhach, x.KmRong, x.TongKm,
                x.ThanhTien, x.DiemDau, x.DiemCuoi, x.DriverNames, x.DriverEmployeeCodes,
                x.DriverPhones, x.ShiftSoTai, x.DriverCount, x.DriverMatchStatus,
                x.DriverShiftStartAt, x.DriverShiftNextAt, x.AppUserName, x.AppVehicleNo,
                x.AppVehicleCode, x.AppTripId, x.AppMatchStatus))
            .ToListAsync(cancellationToken);

        return new ArchiveTaxiTripPage(
            area.AreaCode, area.AreaName, rows, totalRows, page, pageSize, search);
    }
}
