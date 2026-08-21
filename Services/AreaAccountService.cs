using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class AreaAccountService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public AreaAccountService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<AreaContext?> ResolveAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var mapping = await db.AreaAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserName == normalized && x.IsActive, cancellationToken);

        return mapping is null
            ? null
            : new AreaContext(normalized, mapping.AreaCode, mapping.AreaName);
    }
}
