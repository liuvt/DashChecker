using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class CredentialStoreService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IDataProtector _protector;

    public CredentialStoreService(
        IDbContextFactory<LocalDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector("DashChecker.LocalCredential.v1");
    }

    public async Task<SavedCredential> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.LocalCredentials.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (item is null)
        {
            var deviceId = CreateDeviceId();
            return new SavedCredential(string.Empty, string.Empty, false, deviceId);
        }

        var password = string.Empty;
        if (item.RememberPassword && !string.IsNullOrWhiteSpace(item.ProtectedPassword))
        {
            try
            {
                password = _protector.Unprotect(item.ProtectedPassword);
            }
            catch
            {
                password = string.Empty;
            }
        }

        return new SavedCredential(
            item.RememberPassword ? item.UserName : string.Empty,
            password,
            item.RememberPassword,
            item.DeviceId);
    }

    public async Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.LocalCredentials.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (item is not null && !string.IsNullOrWhiteSpace(item.DeviceId))
            return item.DeviceId;

        var deviceId = CreateDeviceId();
        if (item is null)
        {
            item = new LocalCredential
            {
                Id = 1,
                DeviceId = deviceId,
                UpdatedAt = VietnamClock.Now
            };
            db.LocalCredentials.Add(item);
        }
        else
        {
            item.DeviceId = deviceId;
            item.UpdatedAt = VietnamClock.Now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return deviceId;
    }

    public async Task SaveAfterSuccessfulLoginAsync(
        string userName,
        string password,
        bool rememberPassword,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.LocalCredentials.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (item is null)
        {
            item = new LocalCredential { Id = 1 };
            db.LocalCredentials.Add(item);
        }

        item.DeviceId = deviceId;
        item.RememberPassword = rememberPassword;
        item.UserName = rememberPassword ? userName.Trim() : string.Empty;
        item.ProtectedPassword = rememberPassword ? _protector.Protect(password) : string.Empty;
        item.UpdatedAt = VietnamClock.Now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreateDeviceId() => Guid.NewGuid().ToString("N");
}
