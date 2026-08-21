using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class ShiftStoreService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public ShiftStoreService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task ReplaceCurrentAsync(
        AreaContext area,
        ParsedShiftReport report,
        CancellationToken cancellationToken = default)
    {
        var createdAt = VietnamClock.Now;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Current là workspace duy nhất của khu vực. Cập nhật mới thay toàn bộ Current,
        // Kho không bị ảnh hưởng cho đến khi người dùng bấm Lưu.
        await db.ShiftCurrents
            .Where(x => x.AreaCode == area.AreaCode)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ShiftCurrentSyncs
            .Where(x => x.AreaCode == area.AreaCode)
            .ExecuteDeleteAsync(cancellationToken);

        var sync = new ShiftCurrentSync
        {
            AreaCode = area.AreaCode,
            AreaName = area.AreaName,
            SourceUserName = area.UserName,
            SpreadsheetId = report.SpreadsheetId,
            SheetName = report.SheetName,
            SourceDate = report.SourceDate.Date,
            RowCount = report.Rows.Count,
            CreatedAt = createdAt,
            SavedAt = null
        };
        db.ShiftCurrentSyncs.Add(sync);
        await db.SaveChangesAsync(cancellationToken);

        var entities = report.Rows.Select(x => new ShiftCurrent
        {
            SyncId = sync.Id,
            AreaCode = area.AreaCode,
            SourceRow = x.SourceRow,
            SourceDate = x.SourceDate,
            SoTai = x.SoTai,
            SoCho = x.SoCho,
            BienKiemSoat = x.BienKiemSoat,
            BienKiemSoatNormalized = x.BienKiemSoatNormalized,
            HoTenMsnv = x.HoTenMsnv,
            DriverName = x.DriverName,
            EmployeeCode = x.EmployeeCode,
            DriverPhone = x.DriverPhone,
            TrangThaiLenXuongCa = x.TrangThaiLenXuongCa,
            LoaiHinhHopTac = x.LoaiHinhHopTac,
            HinhThucKinhDoanh = x.HinhThucKinhDoanh,
            LyDoXuongCa = x.LyDoXuongCa,
            GhiChu = x.GhiChu,
            SourceTime = x.SourceTime,
            SourceAt = x.SourceAt,
            HinhThucLuong = x.HinhThucLuong,
            IsActive = x.IsActive,
            CreatedAt = createdAt
        }).ToList();

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.ShiftCurrents.AddRange(entities);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> SaveCurrentToArchiveAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var currentSync = await db.ShiftCurrentSyncs.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentSync is null)
            throw new InvalidOperationException($"{area.AreaName} chưa có Current lên/xuống ca để lưu.");

        var currentRows = await db.ShiftCurrents.AsNoTracking()
            .Where(x => x.SyncId == currentSync.Id)
            .OrderBy(x => x.SourceRow)
            .ToListAsync(cancellationToken);

        if (currentRows.Count == 0)
            throw new InvalidOperationException("Current lên/xuống ca đang rỗng.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Kho được replace theo NGÀY TẠO (CreatedAt) của workspace Current.
        // Đồng thời xóa cùng SourceDate nếu có dữ liệu từ phiên bản cũ để tránh trùng unique index.
        var archiveDayStart = currentSync.CreatedAt.Date;
        var archiveDayEnd = archiveDayStart.AddDays(1);
        var oldSyncIds = await db.ShiftArchiveSyncs
            .Where(x => x.AreaCode == area.AreaCode &&
                        ((x.CreatedAt >= archiveDayStart && x.CreatedAt < archiveDayEnd) ||
                         x.SourceDate == currentSync.SourceDate))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (oldSyncIds.Count > 0)
        {
            await db.ShiftArchives.Where(x => oldSyncIds.Contains(x.SyncId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.ShiftArchiveSyncs.Where(x => oldSyncIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var archiveSync = new ShiftArchiveSync
        {
            AreaCode = currentSync.AreaCode,
            AreaName = currentSync.AreaName,
            SourceUserName = currentSync.SourceUserName,
            SpreadsheetId = currentSync.SpreadsheetId,
            SheetName = currentSync.SheetName,
            SourceDate = currentSync.SourceDate,
            RowCount = currentRows.Count,
            CreatedAt = currentSync.CreatedAt,
            SavedAt = now
        };
        db.ShiftArchiveSyncs.Add(archiveSync);
        await db.SaveChangesAsync(cancellationToken);

        var rows = currentRows.Select(x => new ShiftArchive
        {
            SyncId = archiveSync.Id,
            AreaCode = x.AreaCode,
            SourceRow = x.SourceRow,
            SourceDate = x.SourceDate,
            SoTai = x.SoTai,
            SoCho = x.SoCho,
            BienKiemSoat = x.BienKiemSoat,
            BienKiemSoatNormalized = x.BienKiemSoatNormalized,
            HoTenMsnv = x.HoTenMsnv,
            DriverName = x.DriverName,
            EmployeeCode = x.EmployeeCode,
            DriverPhone = x.DriverPhone,
            TrangThaiLenXuongCa = x.TrangThaiLenXuongCa,
            LoaiHinhHopTac = x.LoaiHinhHopTac,
            HinhThucKinhDoanh = x.HinhThucKinhDoanh,
            LyDoXuongCa = x.LyDoXuongCa,
            GhiChu = x.GhiChu,
            SourceTime = x.SourceTime,
            SourceAt = x.SourceAt,
            HinhThucLuong = x.HinhThucLuong,
            IsActive = x.IsActive,
            CreatedAt = currentSync.CreatedAt,
            SavedAt = now
        }).ToList();

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.ShiftArchives.AddRange(rows);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken);

        var trackedSync = await db.ShiftCurrentSyncs.FirstAsync(x => x.Id == currentSync.Id, cancellationToken);
        trackedSync.SavedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<long> AddCurrentRowAsync(
        AreaContext area,
        ShiftCurrentEditModel input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeInput(area, input);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var sync = await db.ShiftCurrentSyncs
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sync is null)
        {
            var now = VietnamClock.Now;
            sync = new ShiftCurrentSync
            {
                AreaCode = area.AreaCode,
                AreaName = area.AreaName,
                SourceUserName = area.UserName,
                SpreadsheetId = "manual",
                SheetName = "QL_LEN_XUONG_CA",
                SourceDate = normalized.SourceDate,
                RowCount = 0,
                CreatedAt = now,
                SavedAt = null
            };
            db.ShiftCurrentSyncs.Add(sync);
            await db.SaveChangesAsync(cancellationToken);
        }

        var nextSourceRow = (await db.ShiftCurrents
            .Where(x => x.SyncId == sync.Id)
            .Select(x => (int?)x.SourceRow)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var entity = CreateCurrentEntity(sync, area, nextSourceRow, normalized);
        db.ShiftCurrents.Add(entity);
        sync.RowCount = await db.ShiftCurrents.CountAsync(x => x.SyncId == sync.Id, cancellationToken) + 1;
        sync.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateCurrentRowAsync(
        AreaContext area,
        long id,
        ShiftCurrentEditModel input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeInput(area, input);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ShiftCurrents
            .FirstOrDefaultAsync(x => x.Id == id && x.AreaCode == area.AreaCode, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy dòng Current cần sửa.");

        var sync = await db.ShiftCurrentSyncs
            .FirstOrDefaultAsync(x => x.Id == entity.SyncId && x.AreaCode == area.AreaCode, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy snapshot Current của dòng dữ liệu.");

        ApplyInput(entity, normalized);
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
        var entity = await db.ShiftCurrents
            .FirstOrDefaultAsync(x => x.Id == id && x.AreaCode == area.AreaCode, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy dòng Current cần xóa.");

        var sync = await db.ShiftCurrentSyncs
            .FirstAsync(x => x.Id == entity.SyncId && x.AreaCode == area.AreaCode, cancellationToken);

        db.ShiftCurrents.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        sync.RowCount = await db.ShiftCurrents.CountAsync(x => x.SyncId == sync.Id, cancellationToken);
        sync.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CurrentShiftPage?> GetCurrentPageAsync(
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
        var sync = await db.ShiftCurrentSyncs.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sync is null) return null;

        var baseQuery = db.ShiftCurrents.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode && x.SyncId == sync.Id);

        var all = await baseQuery.ToListAsync(cancellationToken);
        var summary = BuildSummary(all);
        var plateCounts = BuildPlateDriverCounts(all);
        var soTaiCounts = all
            .Where(x => !string.IsNullOrWhiteSpace(x.SoTai))
            .GroupBy(x => x.SoTai, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        IQueryable<ShiftCurrent> filtered = baseQuery;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            filtered = filtered.Where(x =>
                EF.Functions.Like(x.SoTai, term) ||
                EF.Functions.Like(x.BienKiemSoat, term) ||
                EF.Functions.Like(x.HoTenMsnv, term) ||
                EF.Functions.Like(x.DriverPhone, term) ||
                EF.Functions.Like(x.TrangThaiLenXuongCa, term) ||
                EF.Functions.Like(x.LyDoXuongCa, term) ||
                EF.Functions.Like(x.GhiChu, term));
        }

        var totalRows = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rawRows = await filtered
            .OrderBy(x => x.SoTai)
            .ThenBy(x => x.BienKiemSoat)
            .ThenBy(x => x.SourceAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var rows = rawRows.Select(x => ToListItem(
            x,
            plateCounts.GetValueOrDefault(x.BienKiemSoatNormalized, 0),
            soTaiCounts.GetValueOrDefault(x.SoTai, 0))).ToList();

        return new CurrentShiftPage(
            area.AreaCode, area.AreaName, sync.Id, sync.SourceDate, sync.CreatedAt, sync.SavedAt,
            rows, summary, totalRows, page, pageSize, search);
    }

    public async Task<ArchiveShiftPage> GetArchivePageAsync(
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
        IQueryable<ShiftArchive> query = db.ShiftArchives.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.SoTai, term) ||
                EF.Functions.Like(x.BienKiemSoat, term) ||
                EF.Functions.Like(x.HoTenMsnv, term) ||
                EF.Functions.Like(x.DriverPhone, term) ||
                EF.Functions.Like(x.TrangThaiLenXuongCa, term) ||
                EF.Functions.Like(x.LyDoXuongCa, term) ||
                EF.Functions.Like(x.GhiChu, term));
        }

        var totalRows = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rawRows = await query
            .OrderByDescending(x => x.SourceDate)
            .ThenBy(x => x.SoTai)
            .ThenBy(x => x.SourceAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dates = rawRows.Select(x => x.SourceDate.Date).Distinct().ToList();
        var relevant = dates.Count == 0
            ? new List<ShiftArchive>()
            : await db.ShiftArchives.AsNoTracking()
                .Where(x => x.AreaCode == area.AreaCode && dates.Contains(x.SourceDate))
                .ToListAsync(cancellationToken);

        var plateCountsByDate = relevant
            .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.DriverName) && !string.IsNullOrWhiteSpace(x.BienKiemSoatNormalized))
            .GroupBy(x => $"{x.SourceDate:yyyyMMdd}|{x.BienKiemSoatNormalized}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => g.Select(x => string.IsNullOrWhiteSpace(x.EmployeeCode) ? x.DriverName : x.EmployeeCode)
                      .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.OrdinalIgnoreCase);

        var soTaiCountsByDate = relevant
            .Where(x => !string.IsNullOrWhiteSpace(x.SoTai))
            .GroupBy(x => $"{x.SourceDate:yyyyMMdd}|{x.SoTai}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var rows = rawRows.Select(x => new ShiftArchiveListItem(
            x.Id, x.CreatedAt, x.SavedAt, x.SourceRow, x.SourceDate, x.SoTai, x.SoCho,
            x.BienKiemSoat, x.HoTenMsnv, x.DriverName, x.EmployeeCode, x.DriverPhone,
            x.TrangThaiLenXuongCa, x.LoaiHinhHopTac, x.HinhThucKinhDoanh, x.LyDoXuongCa,
            x.GhiChu, x.SourceTime, x.SourceAt, x.HinhThucLuong, x.IsActive,
            plateCountsByDate.GetValueOrDefault($"{x.SourceDate:yyyyMMdd}|{x.BienKiemSoatNormalized}", 0),
            soTaiCountsByDate.GetValueOrDefault($"{x.SourceDate:yyyyMMdd}|{x.SoTai}", 0))).ToList();

        return new ArchiveShiftPage(area.AreaCode, area.AreaName, rows, totalRows, page, pageSize, search);
    }

    private sealed record NormalizedShiftInput(
        DateTime SourceDate,
        TimeSpan SourceTime,
        DateTime SourceAt,
        string SoTai,
        string SoCho,
        string BienKiemSoat,
        string BienKiemSoatNormalized,
        string HoTenMsnv,
        string DriverName,
        string EmployeeCode,
        string DriverPhone,
        string TrangThaiLenXuongCa,
        string LoaiHinhHopTac,
        string HinhThucKinhDoanh,
        string LyDoXuongCa,
        string GhiChu,
        string HinhThucLuong,
        bool IsActive);

    private static NormalizedShiftInput NormalizeInput(AreaContext area, ShiftCurrentEditModel input)
    {
        var sourceDate = input.SourceDate.Date;
        if (sourceDate == default)
            throw new InvalidOperationException("Ngày ca không hợp lệ.");

        if (!input.SourceTime.HasValue)
            throw new InvalidOperationException("Vui lòng nhập thời gian lên/xuống ca.");

        var sourceTime = input.SourceTime.Value.ToTimeSpan();

        var soTai = input.SoTai?.Trim() ?? string.Empty;
        var plate = input.BienKiemSoat?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(soTai) && string.IsNullOrWhiteSpace(plate))
            throw new InvalidOperationException("Cần nhập ít nhất Số tài hoặc Biển kiểm soát.");

        var rawDriver = input.HoTenMsnv?.Trim() ?? string.Empty;
        var (driverName, employeeCode) = ParseDriver(rawDriver);
        if (!string.IsNullOrWhiteSpace(soTai) || !string.IsNullOrWhiteSpace(employeeCode))
        {
            var belongs = soTai.StartsWith(area.AreaCode, StringComparison.OrdinalIgnoreCase) ||
                          employeeCode.StartsWith(area.AreaCode, StringComparison.OrdinalIgnoreCase);
            if (!belongs)
                throw new InvalidOperationException($"Số tài/MSNV không thuộc khu vực {area.AreaName} ({area.AreaCode}).");
        }

        var status = input.TrangThaiLenXuongCa?.Trim() ?? string.Empty;
        var isActive = string.Equals(status, "Lên ca", StringComparison.OrdinalIgnoreCase);
        var sourceAt = BuildOperationalSourceAt(sourceDate.Date, sourceTime);

        return new NormalizedShiftInput(
            sourceDate.Date, sourceTime, sourceAt, soTai,
            input.SoCho?.Trim() ?? string.Empty,
            plate, VehicleKey.Normalize(plate), rawDriver, driverName, employeeCode,
            NormalizePhone(input.DriverPhone), status,
            input.LoaiHinhHopTac?.Trim() ?? string.Empty,
            input.HinhThucKinhDoanh?.Trim() ?? string.Empty,
            input.LyDoXuongCa?.Trim() ?? string.Empty,
            input.GhiChu?.Trim() ?? string.Empty,
            input.HinhThucLuong?.Trim() ?? string.Empty,
            isActive);
    }

    private static ShiftCurrent CreateCurrentEntity(
        ShiftCurrentSync sync,
        AreaContext area,
        int sourceRow,
        NormalizedShiftInput x)
    {
        var entity = new ShiftCurrent
        {
            SyncId = sync.Id,
            AreaCode = area.AreaCode,
            SourceRow = sourceRow,
            CreatedAt = sync.CreatedAt
        };
        ApplyInput(entity, x);
        return entity;
    }

    private static void ApplyInput(ShiftCurrent entity, NormalizedShiftInput x)
    {
        entity.SourceDate = x.SourceDate;
        entity.SoTai = x.SoTai;
        entity.SoCho = x.SoCho;
        entity.BienKiemSoat = x.BienKiemSoat;
        entity.BienKiemSoatNormalized = x.BienKiemSoatNormalized;
        entity.HoTenMsnv = x.HoTenMsnv;
        entity.DriverName = x.DriverName;
        entity.EmployeeCode = x.EmployeeCode;
        entity.DriverPhone = x.DriverPhone;
        entity.TrangThaiLenXuongCa = x.TrangThaiLenXuongCa;
        entity.LoaiHinhHopTac = x.LoaiHinhHopTac;
        entity.HinhThucKinhDoanh = x.HinhThucKinhDoanh;
        entity.LyDoXuongCa = x.LyDoXuongCa;
        entity.GhiChu = x.GhiChu;
        entity.SourceTime = x.SourceTime;
        entity.SourceAt = x.SourceAt;
        entity.HinhThucLuong = x.HinhThucLuong;
        entity.IsActive = x.IsActive;
    }

    private static DateTime BuildOperationalSourceAt(DateTime sourceDate, TimeSpan sourceTime)
        => sourceTime < TimeSpan.FromHours(5)
            ? sourceDate.Date.AddDays(1).Add(sourceTime)
            : sourceDate.Date.Add(sourceTime);

    private static (string DriverName, string EmployeeCode) ParseDriver(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (string.Empty, string.Empty);
        var separator = raw.LastIndexOf(" - ", StringComparison.Ordinal);
        return separator < 0
            ? (raw.Trim(), string.Empty)
            : (raw[..separator].Trim(), raw[(separator + 3)..].Trim());
    }

    private static string NormalizePhone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 9 ? "0" + digits : digits;
    }

    private static ShiftSummary BuildSummary(IReadOnlyCollection<ShiftCurrent> rows)
    {
        var active = rows.Where(x => x.IsActive).ToList();
        var plateDriverCounts = BuildPlateDriverCounts(rows);
        return new ShiftSummary(
            rows.Count,
            rows.Count(x => string.Equals(x.TrangThaiLenXuongCa, "Lên ca", StringComparison.OrdinalIgnoreCase)),
            rows.Count(x => string.Equals(x.TrangThaiLenXuongCa, "Xuống ca", StringComparison.OrdinalIgnoreCase)),
            rows.Select(x => x.BienKiemSoatNormalized).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            active.Select(x => string.IsNullOrWhiteSpace(x.EmployeeCode) ? x.DriverName : x.EmployeeCode)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            plateDriverCounts.Count(x => x.Value > 1));
    }

    private static Dictionary<string, int> BuildPlateDriverCounts(IEnumerable<ShiftCurrent> rows)
    {
        return rows
            .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.DriverName) && !string.IsNullOrWhiteSpace(x.BienKiemSoatNormalized))
            .GroupBy(x => x.BienKiemSoatNormalized, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => g.Select(x => string.IsNullOrWhiteSpace(x.EmployeeCode) ? x.DriverName : x.EmployeeCode)
                      .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static ShiftListItem ToListItem(ShiftCurrent x, int driverCount, int soTaiCount)
        => new(
            x.Id, x.SourceRow, x.SourceDate, x.SoTai, x.SoCho, x.BienKiemSoat, x.HoTenMsnv,
            x.DriverName, x.EmployeeCode, x.DriverPhone, x.TrangThaiLenXuongCa, x.LoaiHinhHopTac,
            x.HinhThucKinhDoanh, x.LyDoXuongCa, x.GhiChu, x.SourceTime, x.SourceAt,
            x.HinhThucLuong, x.IsActive, driverCount, soTaiCount);
}
