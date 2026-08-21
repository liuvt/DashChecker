namespace DashChecker.Models;

public sealed record SavedCredential(
    string UserName,
    string Password,
    bool RememberPassword,
    string DeviceId);
