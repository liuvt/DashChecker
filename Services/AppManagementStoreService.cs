using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class AppManagementStoreService(IDbContextFactory<LocalDbContext> factory)
{
    public async Task<AppManagedSnapshot> GetAsync(
        AreaContext area,
        string moduleKey,
        bool archive,
        int page = 1,
        int pageSize = 100,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 20, 500);
        search = search?.Trim() ?? string.Empty;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.AppManagedRecords.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && x.IsArchived == archive);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.DataJson.Contains(search));

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new { x.Id, x.RowOrder, x.DataJson, x.CreatedAt, x.SavedAt })
            .ToListAsync(cancellationToken);

        return new AppManagedSnapshot(
            rows.Select(x => new AppManagedRowView(x.Id, x.RowOrder, AppManagedJson.Deserialize(x.DataJson), x.CreatedAt, x.SavedAt)).ToList(),
            total, page, pageSize, search, archive);
    }

    public async Task<List<AppManagedRowView>> GetAllAsync(
        AreaContext area,
        string moduleKey,
        bool archive = false,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        search = search?.Trim() ?? string.Empty;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.AppManagedRecords.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && x.IsArchived == archive);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.DataJson.Contains(search));

        var rows = await query
            .OrderBy(x => x.RowOrder)
            .ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.RowOrder, x.DataJson, x.CreatedAt, x.SavedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new AppManagedRowView(x.Id, x.RowOrder, AppManagedJson.Deserialize(x.DataJson), x.CreatedAt, x.SavedAt))
            .ToList();
    }

    public async Task<int> ReplaceCurrentRowsAsync(
        AreaContext area,
        string moduleKey,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        // Lọc trùng bản ghi nguồn trước khi ghi vào database local (tránh lưu chồng khi chạy 2 lần)
        var deduped = rows
            .GroupBy(values => BuildRowDedupKey(moduleKey, values), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.AppManagedRecords
            .Where(x => x.AreaCode == area.AreaCode &&
                        x.ModuleKey == moduleKey &&
                        !x.IsArchived &&
                        (x.CreatedAt >= dayStart && x.CreatedAt < dayEnd || x.CreatedAt < dayEnd))
            .ExecuteDeleteAsync(cancellationToken);

        var entities = deduped.Select((values, idx) => new AppManagedRecord
        {
            AreaCode = area.AreaCode,
            ModuleKey = moduleKey,
            RowOrder = idx + 1,
            DataJson = AppManagedJson.Serialize(values),
            CreatedAt = now,
            IsArchived = false
        }).ToList();

        if (entities.Count > 0)
        {
            db.AppManagedRecords.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return entities.Count;
    }

    private static string BuildRowDedupKey(string moduleKey, IReadOnlyDictionary<string, string> values)
    {
        string GetVal(string key) => values.TryGetValue(key, out var v) ? (v ?? string.Empty).Trim() : string.Empty;

        var idKey = moduleKey.ToLowerInvariant() switch
        {
            "customer-promo" => $"{GetVal("TripId")}|{GetVal("PartnerCode")}|{GetVal("DriverPhone")}",
            "vnpay" => $"{GetVal("PartnerId")}|{GetVal("SystemId")}|{GetVal("TripOccurredAt")}",
            "xanh-sm" => $"{GetVal("Id")}|{GetVal("Depot")}|{GetVal("BookingTime")}",
            "contracts" => $"{GetVal("Id")}|{GetVal("Date")}|{GetVal("Time")}|{GetVal("VehicleNo")}",
            _ => $"{GetVal("Id")}|{GetVal("VehicleNo")}|{GetVal("BookingTime")}"
        };

        return string.IsNullOrWhiteSpace(idKey.Replace("|", "")) ? AppManagedJson.Serialize(values) : idKey;
    }

    public async Task<long> AddCurrentAsync(
        AreaContext area,
        string moduleKey,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var nextOrder = (await db.AppManagedRecords
            .Where(x => x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && !x.IsArchived)
            .MaxAsync(x => (int?)x.RowOrder, cancellationToken) ?? 0) + 1;

        var row = new AppManagedRecord
        {
            AreaCode = area.AreaCode,
            ModuleKey = moduleKey,
            RowOrder = nextOrder,
            DataJson = AppManagedJson.Serialize(values),
            CreatedAt = VietnamClock.Now,
            IsArchived = false
        };
        db.AppManagedRecords.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task UpdateCurrentAsync(
        AreaContext area,
        string moduleKey,
        long id,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.AppManagedRecords.FirstOrDefaultAsync(
            x => x.Id == id && x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && !x.IsArchived,
            cancellationToken) ?? throw new InvalidOperationException("Không tìm thấy dòng Current cần sửa.");

        row.DataJson = AppManagedJson.Serialize(values);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCurrentAsync(
        AreaContext area,
        string moduleKey,
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.AppManagedRecords.FirstOrDefaultAsync(
            x => x.Id == id && x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && !x.IsArchived,
            cancellationToken);
        if (row is null) return;
        db.AppManagedRecords.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SaveCurrentToArchiveAsync(
        AreaContext area,
        string moduleKey,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var current = await db.AppManagedRecords.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && !x.IsArchived)
            .OrderBy(x => x.RowOrder)
            .ToListAsync(cancellationToken);

        if (current.Count == 0)
            return 0;

        // Kiểm tra ngày tạo (CreatedAt): nếu chạy 2 lần trong cùng ngày tạo thì xóa/cập nhật lại bản chốt của ngày đó, không lưu chồng
        var createdDate = current.Max(x => x.CreatedAt).Date;
        var nextDate = createdDate.AddDays(1);
        var savedAt = VietnamClock.Now;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AppManagedRecords
            .Where(x => x.AreaCode == area.AreaCode &&
                        x.ModuleKey == moduleKey &&
                        x.IsArchived &&
                        x.CreatedAt >= createdDate && x.CreatedAt < nextDate)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var source in current)
        {
            db.AppManagedRecords.Add(new AppManagedRecord
            {
                AreaCode = source.AreaCode,
                ModuleKey = source.ModuleKey,
                RowOrder = source.RowOrder,
                DataJson = source.DataJson,
                CreatedAt = source.CreatedAt,
                IsArchived = true,
                SavedAt = savedAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return current.Count;
    }
}
