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

        var oldArchive = await db.AppManagedRecords
            .Where(x => x.AreaCode == area.AreaCode && x.ModuleKey == moduleKey && x.IsArchived)
            .ToListAsync(cancellationToken);
        if (oldArchive.Count > 0) db.AppManagedRecords.RemoveRange(oldArchive);

        var savedAt = VietnamClock.Now;
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
        return current.Count;
    }
}
