using Microsoft.EntityFrameworkCore;
using DashChecker.Entities;

namespace DashChecker.Data;

public sealed class LocalDbContext(DbContextOptions<LocalDbContext> options)
    : DbContext(options)
{
    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();
    public DbSet<AreaAccount> AreaAccounts => Set<AreaAccount>();

    // Legacy tables retained for one-time migration compatibility.
    public DbSet<TaxiTripSync> TaxiTripSyncs => Set<TaxiTripSync>();
    public DbSet<TaxiTrip> TaxiTrips => Set<TaxiTrip>();

    // Working/current data.
    public DbSet<TaxiTripCurrentSync> TaxiTripCurrentSyncs => Set<TaxiTripCurrentSync>();
    public DbSet<TaxiTripCurrent> TaxiTripCurrents => Set<TaxiTripCurrent>();

    // Warehouse/archive data.
    public DbSet<TaxiTripArchiveSync> TaxiTripArchiveSyncs => Set<TaxiTripArchiveSync>();
    public DbSet<TaxiTripArchive> TaxiTripArchives => Set<TaxiTripArchive>();

    // Driver shift current/archive.
    public DbSet<ShiftCurrentSync> ShiftCurrentSyncs => Set<ShiftCurrentSync>();
    public DbSet<ShiftCurrent> ShiftCurrents => Set<ShiftCurrent>();
    public DbSet<ShiftArchiveSync> ShiftArchiveSyncs => Set<ShiftArchiveSync>();
    public DbSet<ShiftArchive> ShiftArchives => Set<ShiftArchive>();

    // data.skysoft.vn - Xe online Current/Kho.
    public DbSet<OnlineVehicleCurrent> OnlineVehicleCurrents => Set<OnlineVehicleCurrent>();
    public DbSet<OnlineVehicleArchive> OnlineVehicleArchives => Set<OnlineVehicleArchive>();

    // data.skysoft.vn - Báo cáo cuốc khách Current/Kho.
    public DbSet<AppTripCurrent> AppTripCurrents => Set<AppTripCurrent>();
    public DbSet<AppTripArchive> AppTripArchives => Set<AppTripArchive>();

    // Manual app-management tables (Current/Kho by module and area).
    public DbSet<AppManagedRecord> AppManagedRecords => Set<AppManagedRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var credential = modelBuilder.Entity<LocalCredential>();
        credential.ToTable("LocalCredentials");
        credential.HasKey(x => x.Id);
        credential.Property(x => x.UserName).HasMaxLength(200);
        credential.Property(x => x.ProtectedPassword).HasMaxLength(4000);
        credential.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();

        var account = modelBuilder.Entity<AreaAccount>();
        account.ToTable("AreaAccounts");
        account.HasKey(x => x.Id);
        account.HasIndex(x => x.UserName).IsUnique();
        account.HasIndex(x => x.AreaCode);
        account.Property(x => x.UserName).HasMaxLength(200).IsRequired();
        account.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        account.Property(x => x.AreaName).HasMaxLength(200).IsRequired();

        // Legacy tables.
        var legacySync = modelBuilder.Entity<TaxiTripSync>();
        legacySync.ToTable("TaxiTripSyncs");
        legacySync.HasKey(x => x.Id);
        legacySync.HasIndex(x => x.CreatedAt);
        legacySync.Property(x => x.FileId).HasMaxLength(2000).IsRequired();
        legacySync.Property(x => x.ColumnNamesJson).IsRequired();

        var legacyTrip = modelBuilder.Entity<TaxiTrip>();
        legacyTrip.ToTable("TaxiTrips");
        legacyTrip.HasKey(x => x.Id);
        legacyTrip.HasIndex(x => new { x.SyncId, x.RowOrder });
        legacyTrip.HasIndex(x => new { x.SyncId, x.SoHieu, x.BatDau });
        legacyTrip.HasIndex(x => new { x.SyncId, x.BienSo });
        legacyTrip.HasIndex(x => x.BatDau);
        legacyTrip.HasIndex(x => x.CreatedAt);
        legacyTrip.Property(x => x.SoHieu).HasMaxLength(50).IsRequired();
        legacyTrip.Property(x => x.BienSo).HasMaxLength(50).IsRequired();
        legacyTrip.Property(x => x.KmCoKhach).HasPrecision(18, 3);
        legacyTrip.Property(x => x.KmRong).HasPrecision(18, 3);
        legacyTrip.Property(x => x.TongKm).HasPrecision(18, 3);
        legacyTrip.Property(x => x.ThanhTien).HasPrecision(18, 0);
        legacyTrip.Property(x => x.DiemDau).HasMaxLength(1000);
        legacyTrip.Property(x => x.DiemCuoi).HasMaxLength(1000);
        legacySync.HasMany(x => x.Trips)
            .WithOne(x => x.Sync)
            .HasForeignKey(x => x.SyncId)
            .OnDelete(DeleteBehavior.Cascade);

        var currentSync = modelBuilder.Entity<TaxiTripCurrentSync>();
        currentSync.ToTable("TaxiTripCurrentSyncs");
        currentSync.HasKey(x => x.Id);
        currentSync.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        currentSync.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        currentSync.Property(x => x.AreaName).HasMaxLength(200).IsRequired();
        currentSync.Property(x => x.SourceUserName).HasMaxLength(200).IsRequired();
        currentSync.Property(x => x.FileId).HasMaxLength(2000).IsRequired();

        var currentTrip = modelBuilder.Entity<TaxiTripCurrent>();
        currentTrip.ToTable("TaxiTripCurrents");
        currentTrip.HasKey(x => x.Id);
        currentTrip.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        currentTrip.HasIndex(x => new { x.SyncId, x.RowOrder });
        currentTrip.HasIndex(x => new { x.SyncId, x.SoHieu, x.BatDau });
        currentTrip.HasIndex(x => new { x.SyncId, x.BienSo });
        currentTrip.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        currentTrip.Property(x => x.SoHieu).HasMaxLength(50).IsRequired();
        currentTrip.Property(x => x.BienSo).HasMaxLength(50).IsRequired();
        currentTrip.Property(x => x.KmCoKhach).HasPrecision(18, 3);
        currentTrip.Property(x => x.KmRong).HasPrecision(18, 3);
        currentTrip.Property(x => x.TongKm).HasPrecision(18, 3);
        currentTrip.Property(x => x.ThanhTien).HasPrecision(18, 0);
        currentTrip.Property(x => x.DiemDau).HasMaxLength(1000);
        currentTrip.Property(x => x.DiemCuoi).HasMaxLength(1000);
        currentTrip.Property(x => x.DriverNames).HasMaxLength(2000);
        currentTrip.Property(x => x.DriverEmployeeCodes).HasMaxLength(1000);
        currentTrip.Property(x => x.DriverPhones).HasMaxLength(1000);
        currentTrip.Property(x => x.ShiftSoTai).HasMaxLength(500);
        currentTrip.Property(x => x.DriverMatchStatus).HasMaxLength(200);
        currentTrip.Property(x => x.AppUserName).HasMaxLength(300);
        currentTrip.Property(x => x.AppVehicleNo).HasMaxLength(100);
        currentTrip.Property(x => x.AppVehicleCode).HasMaxLength(100);
        currentTrip.Property(x => x.AppTripId).HasMaxLength(300);
        currentTrip.Property(x => x.AppMatchStatus).HasMaxLength(200);
        currentTrip.HasIndex(x => new { x.AreaCode, x.DriverShiftStartAt });
        currentSync.HasMany(x => x.Trips)
            .WithOne(x => x.Sync)
            .HasForeignKey(x => x.SyncId)
            .OnDelete(DeleteBehavior.Cascade);

        var archiveSync = modelBuilder.Entity<TaxiTripArchiveSync>();
        archiveSync.ToTable("TaxiTripArchiveSyncs");
        archiveSync.HasKey(x => x.Id);
        archiveSync.HasIndex(x => x.LegacySyncId).IsUnique();
        archiveSync.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        archiveSync.HasIndex(x => new { x.AreaCode, x.FromDate, x.ToDate });
        archiveSync.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        archiveSync.Property(x => x.AreaName).HasMaxLength(200).IsRequired();
        archiveSync.Property(x => x.SourceUserName).HasMaxLength(200).IsRequired();
        archiveSync.Property(x => x.FileId).HasMaxLength(2000).IsRequired();

        var archiveTrip = modelBuilder.Entity<TaxiTripArchive>();
        archiveTrip.ToTable("TaxiTripArchives");
        archiveTrip.HasKey(x => x.Id);
        archiveTrip.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        archiveTrip.HasIndex(x => new { x.SyncId, x.RowOrder });
        archiveTrip.HasIndex(x => new { x.AreaCode, x.SoHieu, x.BatDau });
        archiveTrip.HasIndex(x => new { x.AreaCode, x.BienSo });
        archiveTrip.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        archiveTrip.Property(x => x.SoHieu).HasMaxLength(50).IsRequired();
        archiveTrip.Property(x => x.BienSo).HasMaxLength(50).IsRequired();
        archiveTrip.Property(x => x.KmCoKhach).HasPrecision(18, 3);
        archiveTrip.Property(x => x.KmRong).HasPrecision(18, 3);
        archiveTrip.Property(x => x.TongKm).HasPrecision(18, 3);
        archiveTrip.Property(x => x.ThanhTien).HasPrecision(18, 0);
        archiveTrip.Property(x => x.DiemDau).HasMaxLength(1000);
        archiveTrip.Property(x => x.DiemCuoi).HasMaxLength(1000);
        archiveTrip.Property(x => x.DriverNames).HasMaxLength(2000);
        archiveTrip.Property(x => x.DriverEmployeeCodes).HasMaxLength(1000);
        archiveTrip.Property(x => x.DriverPhones).HasMaxLength(1000);
        archiveTrip.Property(x => x.ShiftSoTai).HasMaxLength(500);
        archiveTrip.Property(x => x.DriverMatchStatus).HasMaxLength(200);
        archiveTrip.Property(x => x.AppUserName).HasMaxLength(300);
        archiveTrip.Property(x => x.AppVehicleNo).HasMaxLength(100);
        archiveTrip.Property(x => x.AppVehicleCode).HasMaxLength(100);
        archiveTrip.Property(x => x.AppTripId).HasMaxLength(300);
        archiveTrip.Property(x => x.AppMatchStatus).HasMaxLength(200);
        archiveTrip.HasIndex(x => new { x.AreaCode, x.DriverShiftStartAt });
        archiveSync.HasMany(x => x.Trips)
            .WithOne(x => x.Sync)
            .HasForeignKey(x => x.SyncId)
            .OnDelete(DeleteBehavior.Cascade);

        var shiftCurrentSync = modelBuilder.Entity<ShiftCurrentSync>();
        shiftCurrentSync.ToTable("ShiftCurrentSyncs");
        shiftCurrentSync.HasKey(x => x.Id);
        shiftCurrentSync.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        shiftCurrentSync.HasIndex(x => new { x.AreaCode, x.SourceDate });
        shiftCurrentSync.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        shiftCurrentSync.Property(x => x.AreaName).HasMaxLength(200).IsRequired();
        shiftCurrentSync.Property(x => x.SourceUserName).HasMaxLength(200).IsRequired();
        shiftCurrentSync.Property(x => x.SpreadsheetId).HasMaxLength(200).IsRequired();
        shiftCurrentSync.Property(x => x.SheetName).HasMaxLength(200).IsRequired();

        var shiftCurrent = modelBuilder.Entity<ShiftCurrent>();
        shiftCurrent.ToTable("ShiftCurrents");
        shiftCurrent.HasKey(x => x.Id);
        shiftCurrent.HasIndex(x => new { x.AreaCode, x.SourceDate });
        shiftCurrent.HasIndex(x => new { x.AreaCode, x.BienKiemSoatNormalized });
        shiftCurrent.HasIndex(x => new { x.AreaCode, x.SoTai });
        shiftCurrent.HasIndex(x => new { x.SyncId, x.SourceRow });
        ConfigureShiftRow(shiftCurrent);
        shiftCurrentSync.HasMany(x => x.Rows).WithOne(x => x.Sync).HasForeignKey(x => x.SyncId).OnDelete(DeleteBehavior.Cascade);

        var shiftArchiveSync = modelBuilder.Entity<ShiftArchiveSync>();
        shiftArchiveSync.ToTable("ShiftArchiveSyncs");
        shiftArchiveSync.HasKey(x => x.Id);
        shiftArchiveSync.HasIndex(x => new { x.AreaCode, x.SourceDate }).IsUnique();
        shiftArchiveSync.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        shiftArchiveSync.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        shiftArchiveSync.Property(x => x.AreaName).HasMaxLength(200).IsRequired();
        shiftArchiveSync.Property(x => x.SourceUserName).HasMaxLength(200).IsRequired();
        shiftArchiveSync.Property(x => x.SpreadsheetId).HasMaxLength(200).IsRequired();
        shiftArchiveSync.Property(x => x.SheetName).HasMaxLength(200).IsRequired();

        var shiftArchive = modelBuilder.Entity<ShiftArchive>();
        shiftArchive.ToTable("ShiftArchives");
        shiftArchive.HasKey(x => x.Id);
        shiftArchive.HasIndex(x => new { x.AreaCode, x.SourceDate });
        shiftArchive.HasIndex(x => new { x.AreaCode, x.BienKiemSoatNormalized });
        shiftArchive.HasIndex(x => new { x.AreaCode, x.SoTai });
        shiftArchive.HasIndex(x => new { x.SyncId, x.SourceRow });
        ConfigureShiftRow(shiftArchive);
        shiftArchiveSync.HasMany(x => x.Rows).WithOne(x => x.Sync).HasForeignKey(x => x.SyncId).OnDelete(DeleteBehavior.Cascade);

        var onlineVehicleCurrent = modelBuilder.Entity<OnlineVehicleCurrent>();
        onlineVehicleCurrent.ToTable("OnlineVehicleCurrents");
        onlineVehicleCurrent.HasKey(x => x.Id);
        onlineVehicleCurrent.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        onlineVehicleCurrent.HasIndex(x => new { x.AreaCode, x.VehicleNo });
        onlineVehicleCurrent.HasIndex(x => new { x.AreaCode, x.PlateNo });
        ConfigureOnlineVehicle(onlineVehicleCurrent);

        var onlineVehicleArchive = modelBuilder.Entity<OnlineVehicleArchive>();
        onlineVehicleArchive.ToTable("OnlineVehicleArchives");
        onlineVehicleArchive.HasKey(x => x.Id);
        onlineVehicleArchive.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        onlineVehicleArchive.HasIndex(x => new { x.AreaCode, x.VehicleNo });
        onlineVehicleArchive.HasIndex(x => new { x.AreaCode, x.PlateNo });
        ConfigureOnlineVehicle(onlineVehicleArchive);

        var appTripCurrent = modelBuilder.Entity<AppTripCurrent>();
        appTripCurrent.ToTable("AppTripCurrents");
        appTripCurrent.HasKey(x => x.Id);
        appTripCurrent.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        appTripCurrent.HasIndex(x => new { x.AreaCode, x.VehicleNo, x.PickupDate });
        appTripCurrent.HasIndex(x => new { x.AreaCode, x.PlateNo, x.PickupDate });
        ConfigureAppTrip(appTripCurrent);

        var appTripArchive = modelBuilder.Entity<AppTripArchive>();
        appTripArchive.ToTable("AppTripArchives");
        appTripArchive.HasKey(x => x.Id);
        appTripArchive.HasIndex(x => new { x.AreaCode, x.CreatedAt });
        appTripArchive.HasIndex(x => new { x.AreaCode, x.VehicleNo, x.PickupDate });
        appTripArchive.HasIndex(x => new { x.AreaCode, x.PlateNo, x.PickupDate });
        ConfigureAppTrip(appTripArchive);

        var appManaged = modelBuilder.Entity<AppManagedRecord>();
        appManaged.ToTable("AppManagedRecords");
        appManaged.HasKey(x => x.Id);
        appManaged.HasIndex(x => new { x.AreaCode, x.ModuleKey, x.IsArchived, x.RowOrder });
        appManaged.HasIndex(x => new { x.AreaCode, x.ModuleKey, x.CreatedAt });
        appManaged.Property(x => x.AreaCode).HasMaxLength(20).IsRequired();
        appManaged.Property(x => x.ModuleKey).HasMaxLength(60).IsRequired();
        appManaged.Property(x => x.DataJson).IsRequired();
    }

    private static void ConfigureOnlineVehicle<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> row)
        where TEntity : class
    {
        foreach (var name in new[] { "AreaCode", "VehicleId", "PlateNo", "VehicleNo", "VehicleCode" })
            row.Property<string>(name).IsRequired();
        row.Property<string>("AreaCode").HasMaxLength(20);
        row.Property<string>("VehicleId").HasMaxLength(100);
        row.Property<string>("PlateNo").HasMaxLength(50);
        row.Property<string>("VehicleNo").HasMaxLength(100);
        row.Property<string>("VehicleCode").HasMaxLength(100);
    }

    private static void ConfigureAppTrip<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> row)
        where TEntity : class
    {
        foreach (var name in new[] { "AreaCode", "GeneratedId", "VehicleNo", "PlateNo", "UserName", "TripId",
                     "FromPlaceName", "ToPlaceName", "DatePart", "TimePart", "VehicleDateKey", "VehicleCode" })
            row.Property<string>(name).IsRequired();
        row.Property<string>("AreaCode").HasMaxLength(20);
        row.Property<string>("GeneratedId").HasMaxLength(50);
        row.Property<string>("VehicleNo").HasMaxLength(100);
        row.Property<string>("PlateNo").HasMaxLength(50);
        row.Property<string>("UserName").HasMaxLength(300);
        row.Property<string>("TripId").HasMaxLength(300);
        row.Property<string>("FromPlaceName").HasMaxLength(1000);
        row.Property<string>("ToPlaceName").HasMaxLength(1000);
        row.Property<string>("DatePart").HasMaxLength(20);
        row.Property<string>("TimePart").HasMaxLength(20);
        row.Property<string>("VehicleDateKey").HasMaxLength(150);
        row.Property<string>("VehicleCode").HasMaxLength(100);
        foreach (var name in new[] { "Km", "EmptyKm", "TotalKm", "WaitTimeMinutes" })
            row.Property<decimal>(name).HasPrecision(18, 2);
        foreach (var name in new[] { "WaitCharge", "Charge", "RealCharge" })
            row.Property<decimal>(name).HasPrecision(18, 0);
    }

    private static void ConfigureShiftRow<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> row)
        where TEntity : class
    {
        // String configuration is handled with property names because Current/Archive share the same schema.
        foreach (var name in new[] { "AreaCode", "SoTai", "SoCho", "BienKiemSoat", "BienKiemSoatNormalized", "HoTenMsnv",
                     "DriverName", "EmployeeCode", "DriverPhone", "TrangThaiLenXuongCa", "LoaiHinhHopTac",
                     "HinhThucKinhDoanh", "LyDoXuongCa", "GhiChu", "HinhThucLuong" })
        {
            row.Property<string>(name).IsRequired();
        }
        row.Property<string>("AreaCode").HasMaxLength(20);
        row.Property<string>("SoTai").HasMaxLength(50);
        row.Property<string>("SoCho").HasMaxLength(50);
        row.Property<string>("BienKiemSoat").HasMaxLength(50);
        row.Property<string>("BienKiemSoatNormalized").HasMaxLength(50);
        row.Property<string>("HoTenMsnv").HasMaxLength(500);
        row.Property<string>("DriverName").HasMaxLength(300);
        row.Property<string>("EmployeeCode").HasMaxLength(100);
        row.Property<string>("DriverPhone").HasMaxLength(30);
        row.Property<string>("TrangThaiLenXuongCa").HasMaxLength(100);
        row.Property<string>("LoaiHinhHopTac").HasMaxLength(300);
        row.Property<string>("HinhThucKinhDoanh").HasMaxLength(200);
        row.Property<string>("LyDoXuongCa").HasMaxLength(1000);
        row.Property<string>("GhiChu").HasMaxLength(2000);
        row.Property<string>("HinhThucLuong").HasMaxLength(200);
    }
}
