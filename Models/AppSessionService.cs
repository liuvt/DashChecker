namespace DashChecker.Models;

public sealed class AppSessionService
{
    public bool IsLoggedIn { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public AreaContext? Area { get; private set; }

    public event Action? Changed;

    public void SetLoggedIn(string userName, AreaContext area)
    {
        IsLoggedIn = true;
        UserName = userName.Trim();
        Area = area;
        Changed?.Invoke();
    }

    public void Clear()
    {
        IsLoggedIn = false;
        UserName = string.Empty;
        Area = null;
        Changed?.Invoke();
    }
}
