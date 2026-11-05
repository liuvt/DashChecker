using DashChecker.Models;

namespace DashChecker.Services;

/// <summary>
/// Dịch vụ nền chạy hẹn giờ tự động (Auto-Sync Worker) cho Ali (admin.nhaxeviet.vn).
/// - Bắt đầu theo mốc thời gian có sẵn (Preset 24h HH:mm:ss), chạy đúng 1 lần hàng ngày.
/// - Chỉ gọi API và lưu dữ liệu cục bộ vào các bảng SQLite (AliOrders, AliPromotes, AliCancelOrders,
///   AliSwitchboards, AliPartnerGsms, AliPartnerVnPays, AliOnlineApps).
/// - KHÔNG tự ý ghi lên Google Sheet (chỉ khi người dùng bấm nút Nhập vào Google Sheet trên giao diện mới ghi).
/// </summary>
public sealed class AliWorkerService
{
    public static readonly IReadOnlyList<(string Key, TimeSpan Time, string Label)> PresetStartTimes =
    [
        ("05:05:00", new TimeSpan(5, 5, 0), "05:05:00 — Mốc chuẩn ca sáng (05:05:00 → 05:40:00, 1 lần/ngày)"),
        ("05:00:00", new TimeSpan(5, 0, 0), "05:00:00 — Đầu ca sáng (05:00:00 → 05:35:00, 1 lần/ngày)"),
        ("05:15:00", new TimeSpan(5, 15, 0), "05:15:00 — Ca sáng 05:15 (05:15:00 → 05:50:00, 1 lần/ngày)"),
        ("05:30:00", new TimeSpan(5, 30, 0), "05:30:00 — Ca sáng 05:30 (05:30:00 → 06:05:00, 1 lần/ngày)"),
        ("06:00:00", new TimeSpan(6, 0, 0), "06:00:00 — Mốc 06:00 sáng (06:00:00 → 06:35:00, 1 lần/ngày)"),
        ("07:00:00", new TimeSpan(7, 0, 0), "07:00:00 — Mốc 07:00 sáng (07:00:00 → 07:35:00, 1 lần/ngày)"),
        ("12:00:00", new TimeSpan(12, 0, 0), "12:00:00 — Mốc 12:00 trưa (12:00:00 → 12:35:00, 1 lần/ngày)"),
        ("17:00:00", new TimeSpan(17, 0, 0), "17:00:00 — Mốc 17:00 chiều (17:00:00 → 17:35:00, 1 lần/ngày)"),
        ("22:00:00", new TimeSpan(22, 0, 0), "22:00:00 — Mốc 22:00 tối (22:00:00 → 22:35:00, 1 lần/ngày)")
    ];

    public static readonly IReadOnlyList<AliScheduleJobInfo> DefaultJobs =
    [
        new("orders", "1. Đơn hàng App KH", "Bảng SQLite: AliOrders", new TimeSpan(5, 5, 0), "9 tỉnh thành · Lưu cục bộ SQLite (không tự ghi Sheet)"),
        new("gsm", "2. Đối tác GSM", "Bảng SQLite: AliPartnerGsms", new TimeSpan(5, 10, 0), "Toàn khu vực · Lưu cục bộ SQLite"),
        new("vnpay", "3. Đối tác VNPay", "Bảng SQLite: AliPartnerVnPays", new TimeSpan(5, 15, 0), "Toàn khu vực · Lưu cục bộ SQLite"),
        new("promotes", "4. Khuyến mãi", "Bảng SQLite: AliPromotes", new TimeSpan(5, 20, 0), "9 tỉnh thành · Lưu cục bộ SQLite"),
        new("switchboard", "5. Tổng đài hoàn thành", "Bảng SQLite: AliSwitchboards", new TimeSpan(5, 25, 0), "9 tỉnh thành · Lưu cục bộ SQLite"),
        new("cancels", "6. Cuốc hủy Tổng đài/App", "Bảng SQLite: AliCancelOrders", new TimeSpan(5, 30, 0), "18 lượt (4, 6, 8) · Lưu cục bộ SQLite"),
        new("online", "7. Thống kê Online App", "Bảng SQLite: AliOnlineApps", new TimeSpan(5, 40, 0), "9 tỉnh thành · Lưu cục bộ SQLite")
    ];

    private readonly AliService _aliService;
    private CancellationTokenSource? _cts;

    public event Action? StatusChanged;

    public bool IsRunning => _cts != null;
    public string CurrentStatus { get; private set; } = "Đang dừng";
    public DateTime? LastUpdated { get; private set; }
    public DateOnly? LastCompletedDailyDate { get; private set; }
    public string SelectedPresetKey { get; private set; } = "05:05:00";
    public TimeSpan SelectedStartTime { get; private set; } = new(5, 5, 0);

    public IReadOnlyList<AliScheduleJobInfo> GetActiveJobsForSelectedPreset()
    {
        var t0 = SelectedStartTime;
        return
        [
            new("orders", "1. Đơn hàng App KH", "AliOrders", t0, "9 tỉnh thành · Lưu cục bộ SQLite"),
            new("gsm", "2. Đối tác GSM", "AliPartnerGsms", t0.Add(TimeSpan.FromMinutes(5)), "Toàn khu vực · Lưu cục bộ SQLite"),
            new("vnpay", "3. Đối tác VNPay", "AliPartnerVnPays", t0.Add(TimeSpan.FromMinutes(10)), "Toàn khu vực · Lưu cục bộ SQLite"),
            new("promotes", "4. Khuyến mãi", "AliPromotes", t0.Add(TimeSpan.FromMinutes(15)), "9 tỉnh thành · Lưu cục bộ SQLite"),
            new("switchboard", "5. Tổng đài hoàn thành", "AliSwitchboards", t0.Add(TimeSpan.FromMinutes(20)), "9 tỉnh thành · Lưu cục bộ SQLite"),
            new("cancels", "6. Cuốc hủy Tổng đài/App", "AliCancelOrders", t0.Add(TimeSpan.FromMinutes(25)), "18 lượt · Lưu cục bộ SQLite"),
            new("online", "7. Thống kê Online App", "AliOnlineApps", t0.Add(TimeSpan.FromMinutes(35)), "9 tỉnh thành · Lưu cục bộ SQLite")
        ];
    }

    public AliWorkerService(AliService aliService)
    {
        _aliService = aliService;
        _aliService.ProgressChanged += OnServiceProgressChanged;
    }

    private void OnServiceProgressChanged(string message)
    {
        if (!IsRunning) return;
        UpdateStatus(message);
    }

    private void UpdateStatus(string status)
    {
        CurrentStatus = status;
        LastUpdated = VietnamClock.Now;
        StatusChanged?.Invoke();
    }

    public void SelectPreset(string presetKey)
    {
        var found = PresetStartTimes.FirstOrDefault(x => string.Equals(x.Key, presetKey, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(found.Key))
        {
            SelectedPresetKey = found.Key;
            SelectedStartTime = found.Time;
        }
        else if (TimeSpan.TryParse(presetKey, out var parsed))
        {
            SelectedPresetKey = parsed.ToString(@"hh\:mm\:ss");
            SelectedStartTime = parsed;
        }
        StatusChanged?.Invoke();
    }

    public void Start(string? presetKey = null)
    {
        if (!string.IsNullOrWhiteSpace(presetKey))
            SelectPreset(presetKey);

        if (_cts != null)
            Stop();

        _cts = new CancellationTokenSource();
        var endSpan = SelectedStartTime.Add(TimeSpan.FromMinutes(35));
        UpdateStatus(
            $"Đã bật hẹn giờ 1 lần/ngày theo mốc {SelectedStartTime:hh\\:mm\\:ss} → {endSpan:hh\\:mm\\:ss} (Chỉ gọi API & lưu SQLite cục bộ)");
        _ = Task.Run(() => RunDailyLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
        UpdateStatus("Đã dừng dịch vụ chạy hẹn giờ");
    }

    private async Task RunDailyLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var now = VietnamClock.Now;
                var targetDate = now.Date;
                var targetDateOnly = DateOnly.FromDateTime(targetDate);

                // Đảm bảo chỉ chạy đúng 1 lần hàng ngày: nếu ngày hôm nay đã hoàn tất hoặc đã qua mốc cuối cùng thì lên lịch cho ngày hôm sau.
                var jobs = BuildScheduledLocalJobs(targetDate, SelectedStartTime);
                var lastJobTime = jobs.Max(x => x.ScheduledAt);

                if (LastCompletedDailyDate == targetDateOnly || now >= lastJobTime)
                {
                    targetDate = targetDate.AddDays(1);
                    targetDateOnly = DateOnly.FromDateTime(targetDate);
                    jobs = BuildScheduledLocalJobs(targetDate, SelectedStartTime);
                }

                foreach (var job in jobs)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (job.ScheduledAt < now)
                        continue;

                    var delay = job.ScheduledAt - VietnamClock.Now;
                    if (delay > TimeSpan.Zero)
                    {
                        UpdateStatus($"Đang chờ mốc {job.Name} vào {job.ScheduledAt:dd/MM/yyyy HH:mm:ss} (1 lần/ngày · Lưu SQLite)");
                        await Task.Delay(delay, token);
                    }

                    if (!token.IsCancellationRequested)
                    {
                        await ExecuteJobSafelyAsync(job.Name, job.Action, token);
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    LastCompletedDailyDate = targetDateOnly;
                    var nextDayStart = targetDate.AddDays(1).Add(SelectedStartTime);
                    UpdateStatus($"Đã hoàn tất chu kỳ 1 lần/ngày ({targetDate:dd/MM/yyyy}) vào SQLite. Lần chạy kế tiếp: {nextDayStart:dd/MM/yyyy HH:mm:ss}");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Lỗi lịch chạy: {ex.Message}");
                await Task.Delay(5000, token);
            }
        }
    }

    /// <summary>
    /// Xây dựng 7 tiến trình theo mốc thời gian đã chọn:
    /// CHỈ gọi API từ admin.nhaxeviet.vn và lưu vào các bảng SQLite cục bộ (KHÔNG tự ý ghi lên Google Sheet).
    /// </summary>
    private (string Name, DateTime ScheduledAt, Func<CancellationToken, Task> Action)[] BuildScheduledLocalJobs(
        DateTime baseDate,
        TimeSpan startTime)
    {
        var t0 = baseDate.Add(startTime);
        return
        [
            ($"1. Đơn hàng App KH ({t0:HH:mm:ss})", t0, async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetOrdersAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), "ALL", ct);
            }),
            ($"2. Đối tác GSM ({t0.AddMinutes(5):HH:mm:ss})", t0.AddMinutes(5), async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetPartnerGsmAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), ct);
            }),
            ($"3. Đối tác VNPay ({t0.AddMinutes(10):HH:mm:ss})", t0.AddMinutes(10), async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetPartnerVnPayAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), ct);
            }),
            ($"4. Khuyến mãi ({t0.AddMinutes(15):HH:mm:ss})", t0.AddMinutes(15), async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetPromotesAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), "ALL", ct);
            }),
            ($"5. Tổng đài hoàn thành ({t0.AddMinutes(20):HH:mm:ss})", t0.AddMinutes(20), async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetSwitchboardsAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), "ALL", ct);
            }),
            ($"6. Cuốc hủy Tổng đài/App ({t0.AddMinutes(25):HH:mm:ss})", t0.AddMinutes(25), async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetCancelOrdersAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), "ALL", ct);
            }),
            ($"7. Online App ({t0.AddMinutes(35):HH:mm:ss})", t0.AddMinutes(35), async ct =>
            {
                var now = VietnamClock.Now;
                await _aliService.GetOnlineAppsAsync(now.Date.AddDays(-1).AddHours(5), now.Date.AddHours(5), "ALL", ct);
            })
        ];
    }

    private async Task ExecuteJobSafelyAsync(
        string jobName,
        Func<CancellationToken, Task> job,
        CancellationToken token)
    {
        try
        {
            UpdateStatus($"Đang gọi API & lưu SQLite: {jobName}...");
            await job(token);
            UpdateStatus($"Đã lưu SQLite {jobName} lúc {VietnamClock.Now:HH:mm:ss}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateStatus($"Lỗi {jobName}: {ex.Message}");
        }
    }
}
