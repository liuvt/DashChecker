using Microsoft.EntityFrameworkCore;
using DashChecker.Data;
using DashChecker.Entities;
using DashChecker.Models;

namespace DashChecker.Services;

public sealed class TripDriverMatcherService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public TripDriverMatcherService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Ghép tài xế cho Current cuốc theo khoảng hiệu lực của mốc LÊN CA.
    /// Ví dụ cùng xe: A lên 05:00, B lên 13:00 => cuốc [05:00,13:00) thuộc A,
    /// cuốc từ 13:00 trở đi thuộc B. Nếu nhiều tài cùng đúng một mốc thì giữ tất cả.
    /// </summary>
    public async Task<int> RefreshCurrentTripAssignmentsAsync(
        AreaContext area,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var tripSync = await db.TaxiTripCurrentSyncs
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tripSync is null) return 0;

        var shiftSync = await db.ShiftCurrentSyncs.AsNoTracking()
            .Where(x => x.AreaCode == area.AreaCode)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var trips = await db.TaxiTripCurrents
            .Where(x => x.SyncId == tripSync.Id && x.AreaCode == area.AreaCode)
            .ToListAsync(cancellationToken);

        if (shiftSync is null)
        {
            foreach (var trip in trips)
                ClearMatch(trip, "Chưa có Current ca");
            tripSync.SavedAt = null;
            await db.SaveChangesAsync(cancellationToken);
            return 0;
        }

        var shifts = await db.ShiftCurrents.AsNoTracking()
            .Where(x => x.SyncId == shiftSync.Id &&
                        x.AreaCode == area.AreaCode &&
                        x.IsActive)
            .OrderBy(x => x.SourceAt)
            .ThenBy(x => x.SourceRow)
            .ToListAsync(cancellationToken);

        var matchedTrips = 0;

        foreach (var trip in trips)
        {
            var operationalDate = GetOperationalDate(trip.BatDau);
            var tripPlate = VehicleKey.Normalize(trip.BienSo);
            var tripSoTai = VehicleKey.Normalize(trip.SoHieu);

            var sameDateRows = shifts
                .Where(x => x.SourceDate.Date == operationalDate.Date)
                .ToList();

            // Biển số là định danh xe vật lý chính. Nếu không có biển số khớp mới fallback Số tài.
            // Điều này đặc biệt quan trọng khi cùng một xe có nhiều tài nhưng dữ liệu Số tài thay đổi.
            var plateRows = sameDateRows
                .Where(x => !string.IsNullOrWhiteSpace(tripPlate) &&
                            x.BienKiemSoatNormalized == tripPlate)
                .ToList();

            var bestVehicleRows = plateRows.Count > 0
                ? plateRows.OrderBy(x => x.SourceAt).ThenBy(x => x.SourceRow).ToList()
                : sameDateRows
                    .Where(x => !string.IsNullOrWhiteSpace(tripSoTai) &&
                                VehicleKey.Normalize(x.SoTai) == tripSoTai)
                    .OrderBy(x => x.SourceAt)
                    .ThenBy(x => x.SourceRow)
                    .ToList();

            if (bestVehicleRows.Count == 0)
            {
                ClearMatch(trip, "Không tìm thấy xe/số tài trong Current ca");
                continue;
            }

            // Tài xế có hiệu lực là mốc lên ca gần nhất NHƯNG KHÔNG SAU thời gian bắt đầu cuốc.
            var effectiveRows = bestVehicleRows
                .Where(x => x.SourceAt <= trip.BatDau)
                .ToList();

            if (effectiveRows.Count == 0)
            {
                ClearMatch(trip, "Cuốc bắt đầu trước mốc lên ca đầu tiên");
                continue;
            }

            var effectiveStart = effectiveRows.Max(x => x.SourceAt);
            var selected = effectiveRows
                .Where(x => x.SourceAt == effectiveStart)
                .ToList();

            var nextStart = bestVehicleRows
                .Where(x => x.SourceAt > effectiveStart)
                .Select(x => (DateTime?)x.SourceAt)
                .Min();

            var drivers = selected
                .Where(x => !string.IsNullOrWhiteSpace(x.DriverName) ||
                            !string.IsNullOrWhiteSpace(x.EmployeeCode))
                .GroupBy(
                    x => string.IsNullOrWhiteSpace(x.EmployeeCode) ? x.DriverName : x.EmployeeCode,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            trip.DriverShiftStartAt = effectiveStart;
            trip.DriverShiftNextAt = nextStart;
            trip.ShiftSoTai = string.Join("; ", selected
                .Select(x => x.SoTai)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase));

            if (drivers.Count == 0)
            {
                trip.DriverNames = string.Empty;
                trip.DriverEmployeeCodes = string.Empty;
                trip.DriverPhones = string.Empty;
                trip.DriverCount = 0;
                trip.DriverMatchStatus = "Mốc ca chưa có tài xế";
                continue;
            }

            trip.DriverNames = string.Join("; ", drivers
                .Select(x => x.DriverName)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            trip.DriverEmployeeCodes = string.Join("; ", drivers
                .Select(x => x.EmployeeCode)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            trip.DriverPhones = string.Join("; ", drivers
                .Select(x => x.DriverPhone)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            trip.DriverCount = drivers.Count;
            trip.DriverMatchStatus = drivers.Count == 1
                ? "Đã ghép theo mốc ca"
                : $"Nhiều tài cùng mốc ({drivers.Count})";
            matchedTrips++;
        }

        // Ghép ca làm thay đổi Current cuốc; nếu đã từng lưu Kho thì cần bấm Lưu cuốc lại để chốt mapping mới.
        tripSync.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return matchedTrips;
    }


    private static DateTime GetOperationalDate(DateTime tripStart)
        => tripStart.TimeOfDay < TimeSpan.FromHours(5)
            ? tripStart.Date.AddDays(-1)
            : tripStart.Date;

    private static void ClearMatch(TaxiTripCurrent trip, string status)
    {
        trip.DriverNames = string.Empty;
        trip.DriverEmployeeCodes = string.Empty;
        trip.DriverPhones = string.Empty;
        trip.ShiftSoTai = string.Empty;
        trip.DriverCount = 0;
        trip.DriverMatchStatus = status;
        trip.DriverShiftStartAt = null;
        trip.DriverShiftNextAt = null;
    }
}
