using Microsoft.AspNetCore.DataProtection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using DashChecker.Components;
using DashChecker.Data;
using DashChecker.Models;
using DashChecker.Services;
using DashChecker.Servers.Interfaces;
using DashChecker.Servers;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor UI services (dialog, snackbar, popover, theme).
builder.Services.AddMudServices();

builder.Services.Configure<SkySoftOptions>(
    builder.Configuration.GetSection("SkySoft"));
builder.Services.Configure<GoogleShiftOptions>(
    builder.Configuration.GetSection("GoogleShift"));
builder.Services.Configure<GgsDashCheckerOptions>(
    builder.Configuration.GetSection("GgsDashChecker"));
builder.Services.Configure<SkySoftAppOptions>(
    builder.Configuration.GetSection("SkySoftApp"));

var appDataFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var keyFolder = Path.Combine(appDataFolder, "Keys");
Directory.CreateDirectory(appDataFolder);
Directory.CreateDirectory(keyFolder);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyFolder))
    .SetApplicationName("DashChecker");

var localDbPath = Path.Combine(appDataFolder, "skysoft-trips.db");
builder.Services.AddDbContextFactory<LocalDbContext>(options =>
    options.UseSqlite($"Data Source={localDbPath}"));

builder.Services.AddScoped<CredentialStoreService>();
builder.Services.AddScoped<AreaAccountService>();
builder.Services.AddScoped<TaxiTripStoreService>();
builder.Services.AddScoped<ShiftStoreService>();
builder.Services.AddScoped<TripDriverMatcherService>();
builder.Services.AddScoped<AppSessionService>();
builder.Services.AddHttpClient<GoogleShiftService>();
builder.Services.AddHttpClient<SkySoftAppService>();
builder.Services.AddScoped<OnlineAppStoreService>();
builder.Services.AddScoped<AppManagementStoreService>();
// Một instance toàn app để khóa thao tác ghi F:O giữa nhiều Blazor circuit.
builder.Services.AddSingleton<IGgsCheckerServer, GgsCheckerServer>();

// Mỗi Blazor circuit có CookieContainer SkySoft riêng.
builder.Services.AddScoped<SkySoftReportService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();

    // EnsureCreated không bổ sung table vào database SQLite đã tồn tại,
    // vì vậy luôn đảm bảo các table Area / Current / Archive tồn tại khi nâng cấp từ bản cũ.
    await db.Database.ExecuteSqlRawAsync(@"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS AreaAccounts (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL,
    AreaCode TEXT NOT NULL,
    AreaName TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    UpdatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AreaAccounts_UserName ON AreaAccounts(UserName);
CREATE INDEX IF NOT EXISTS IX_AreaAccounts_AreaCode ON AreaAccounts(AreaCode);

CREATE TABLE IF NOT EXISTS TaxiTripCurrentSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL,
    AreaName TEXT NOT NULL,
    SourceUserName TEXT NOT NULL,
    FileId TEXT NOT NULL,
    ReportId INTEGER NOT NULL,
    FromDate TEXT NOT NULL,
    ToDate TEXT NOT NULL,
    FromTime TEXT NOT NULL,
    ToTime TEXT NOT NULL,
    RowCount INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    SavedAt TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrentSyncs_AreaCode_CreatedAt
ON TaxiTripCurrentSyncs(AreaCode, CreatedAt);

CREATE TABLE IF NOT EXISTS TaxiTripCurrents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SyncId INTEGER NOT NULL,
    AreaCode TEXT NOT NULL,
    RowOrder INTEGER NOT NULL,
    SoHieu TEXT NOT NULL,
    BienSo TEXT NOT NULL,
    BatDau TEXT NOT NULL,
    KetThuc TEXT NOT NULL,
    KmCoKhach TEXT NOT NULL,
    KmRong TEXT NOT NULL,
    TongKm TEXT NOT NULL,
    ThanhTien TEXT NOT NULL,
    DiemDau TEXT NOT NULL,
    DiemCuoi TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_TaxiTripCurrents_CurrentSyncs_SyncId
        FOREIGN KEY (SyncId) REFERENCES TaxiTripCurrentSyncs(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrents_AreaCode_CreatedAt ON TaxiTripCurrents(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrents_SyncId_RowOrder ON TaxiTripCurrents(SyncId, RowOrder);
CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrents_SyncId_SoHieu_BatDau ON TaxiTripCurrents(SyncId, SoHieu, BatDau);
CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrents_SyncId_BienSo ON TaxiTripCurrents(SyncId, BienSo);

CREATE TABLE IF NOT EXISTS TaxiTripArchiveSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    LegacySyncId INTEGER NULL,
    AreaCode TEXT NOT NULL,
    AreaName TEXT NOT NULL,
    SourceUserName TEXT NOT NULL,
    FileId TEXT NOT NULL,
    ReportId INTEGER NOT NULL,
    FromDate TEXT NOT NULL,
    ToDate TEXT NOT NULL,
    FromTime TEXT NOT NULL,
    ToTime TEXT NOT NULL,
    RowCount INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    SavedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_TaxiTripArchiveSyncs_LegacySyncId ON TaxiTripArchiveSyncs(LegacySyncId);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchiveSyncs_AreaCode_CreatedAt ON TaxiTripArchiveSyncs(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchiveSyncs_AreaCode_FromDate_ToDate ON TaxiTripArchiveSyncs(AreaCode, FromDate, ToDate);

CREATE TABLE IF NOT EXISTS TaxiTripArchives (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SyncId INTEGER NOT NULL,
    AreaCode TEXT NOT NULL,
    RowOrder INTEGER NOT NULL,
    SoHieu TEXT NOT NULL,
    BienSo TEXT NOT NULL,
    BatDau TEXT NOT NULL,
    KetThuc TEXT NOT NULL,
    KmCoKhach TEXT NOT NULL,
    KmRong TEXT NOT NULL,
    TongKm TEXT NOT NULL,
    ThanhTien TEXT NOT NULL,
    DiemDau TEXT NOT NULL,
    DiemCuoi TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    SavedAt TEXT NOT NULL,
    CONSTRAINT FK_TaxiTripArchives_ArchiveSyncs_SyncId
        FOREIGN KEY (SyncId) REFERENCES TaxiTripArchiveSyncs(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchives_AreaCode_CreatedAt ON TaxiTripArchives(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchives_SyncId_RowOrder ON TaxiTripArchives(SyncId, RowOrder);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchives_AreaCode_SoHieu_BatDau ON TaxiTripArchives(AreaCode, SoHieu, BatDau);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchives_AreaCode_BienSo ON TaxiTripArchives(AreaCode, BienSo);

CREATE TABLE IF NOT EXISTS ShiftCurrentSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, AreaName TEXT NOT NULL, SourceUserName TEXT NOT NULL,
    SpreadsheetId TEXT NOT NULL, SheetName TEXT NOT NULL, SourceDate TEXT NOT NULL,
    RowCount INTEGER NOT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrentSyncs_AreaCode_CreatedAt ON ShiftCurrentSyncs(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrentSyncs_AreaCode_SourceDate ON ShiftCurrentSyncs(AreaCode, SourceDate);

CREATE TABLE IF NOT EXISTS ShiftCurrents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, SyncId INTEGER NOT NULL, AreaCode TEXT NOT NULL,
    SourceRow INTEGER NOT NULL, SourceDate TEXT NOT NULL, SoTai TEXT NOT NULL, SoCho TEXT NOT NULL,
    BienKiemSoat TEXT NOT NULL, BienKiemSoatNormalized TEXT NOT NULL, HoTenMsnv TEXT NOT NULL,
    DriverName TEXT NOT NULL, EmployeeCode TEXT NOT NULL, DriverPhone TEXT NOT NULL,
    TrangThaiLenXuongCa TEXT NOT NULL, LoaiHinhHopTac TEXT NOT NULL, HinhThucKinhDoanh TEXT NOT NULL,
    LyDoXuongCa TEXT NOT NULL, GhiChu TEXT NOT NULL, SourceTime TEXT NOT NULL, SourceAt TEXT NOT NULL,
    HinhThucLuong TEXT NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_ShiftCurrents_CurrentSyncs_SyncId FOREIGN KEY (SyncId) REFERENCES ShiftCurrentSyncs(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_AreaCode_SourceDate ON ShiftCurrents(AreaCode, SourceDate);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_AreaCode_Plate ON ShiftCurrents(AreaCode, BienKiemSoatNormalized);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_AreaCode_SoTai ON ShiftCurrents(AreaCode, SoTai);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_SyncId_SourceRow ON ShiftCurrents(SyncId, SourceRow);

CREATE TABLE IF NOT EXISTS ShiftArchiveSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, AreaName TEXT NOT NULL, SourceUserName TEXT NOT NULL,
    SpreadsheetId TEXT NOT NULL, SheetName TEXT NOT NULL, SourceDate TEXT NOT NULL,
    RowCount INTEGER NOT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ShiftArchiveSyncs_AreaCode_SourceDate ON ShiftArchiveSyncs(AreaCode, SourceDate);
CREATE INDEX IF NOT EXISTS IX_ShiftArchiveSyncs_AreaCode_CreatedAt ON ShiftArchiveSyncs(AreaCode, CreatedAt);

CREATE TABLE IF NOT EXISTS ShiftArchives (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, SyncId INTEGER NOT NULL, AreaCode TEXT NOT NULL,
    SourceRow INTEGER NOT NULL, SourceDate TEXT NOT NULL, SoTai TEXT NOT NULL, SoCho TEXT NOT NULL,
    BienKiemSoat TEXT NOT NULL, BienKiemSoatNormalized TEXT NOT NULL, HoTenMsnv TEXT NOT NULL,
    DriverName TEXT NOT NULL, EmployeeCode TEXT NOT NULL, DriverPhone TEXT NOT NULL,
    TrangThaiLenXuongCa TEXT NOT NULL, LoaiHinhHopTac TEXT NOT NULL, HinhThucKinhDoanh TEXT NOT NULL,
    LyDoXuongCa TEXT NOT NULL, GhiChu TEXT NOT NULL, SourceTime TEXT NOT NULL, SourceAt TEXT NOT NULL,
    HinhThucLuong TEXT NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL,
    CONSTRAINT FK_ShiftArchives_ArchiveSyncs_SyncId FOREIGN KEY (SyncId) REFERENCES ShiftArchiveSyncs(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_AreaCode_SourceDate ON ShiftArchives(AreaCode, SourceDate);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_AreaCode_Plate ON ShiftArchives(AreaCode, BienKiemSoatNormalized);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_AreaCode_SoTai ON ShiftArchives(AreaCode, SoTai);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_SyncId_SourceRow ON ShiftArchives(SyncId, SourceRow);

INSERT INTO AreaAccounts(UserName, AreaCode, AreaName, IsActive, UpdatedAt)
VALUES ('namthangbl', 'BL', 'Bạc Liêu', 1, datetime('now'))
ON CONFLICT(UserName) DO UPDATE SET AreaCode='BL', AreaName='Bạc Liêu', IsActive=1, UpdatedAt=datetime('now');

INSERT INTO AreaAccounts(UserName, AreaCode, AreaName, IsActive, UpdatedAt)
VALUES ('namthangvl', 'VL', 'Vĩnh Long', 1, datetime('now'))
ON CONFLICT(UserName) DO UPDATE SET AreaCode='VL', AreaName='Vĩnh Long', IsActive=1, UpdatedAt=datetime('now');

INSERT INTO AreaAccounts(UserName, AreaCode, AreaName, IsActive, UpdatedAt)
VALUES ('namthangvinhlong', 'VL', 'Vĩnh Long', 1, datetime('now'))
ON CONFLICT(UserName) DO UPDATE SET AreaCode='VL', AreaName='Vĩnh Long', IsActive=1, UpdatedAt=datetime('now');
" );

    // data.skysoft.vn: tách riêng Xe online và Báo cáo cuốc khách thành Current/Kho.
    await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS OnlineVehicleCurrents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, VehicleId TEXT NOT NULL, PlateNo TEXT NOT NULL, VehicleNo TEXT NOT NULL,
    VehicleCode TEXT NOT NULL, GpsDate TEXT NULL, UpdateDate TEXT NULL, CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_OnlineVehicleCurrents_AreaCode_CreatedAt ON OnlineVehicleCurrents(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_OnlineVehicleCurrents_AreaCode_VehicleNo ON OnlineVehicleCurrents(AreaCode, VehicleNo);
CREATE INDEX IF NOT EXISTS IX_OnlineVehicleCurrents_AreaCode_PlateNo ON OnlineVehicleCurrents(AreaCode, PlateNo);

CREATE TABLE IF NOT EXISTS OnlineVehicleArchives (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, VehicleId TEXT NOT NULL, PlateNo TEXT NOT NULL, VehicleNo TEXT NOT NULL,
    VehicleCode TEXT NOT NULL, GpsDate TEXT NULL, UpdateDate TEXT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_OnlineVehicleArchives_AreaCode_CreatedAt ON OnlineVehicleArchives(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_OnlineVehicleArchives_AreaCode_VehicleNo ON OnlineVehicleArchives(AreaCode, VehicleNo);
CREATE INDEX IF NOT EXISTS IX_OnlineVehicleArchives_AreaCode_PlateNo ON OnlineVehicleArchives(AreaCode, PlateNo);

CREATE TABLE IF NOT EXISTS AppTripCurrents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, AreaCode TEXT NOT NULL, RowOrder INTEGER NOT NULL,
    GeneratedId TEXT NOT NULL, VehicleNo TEXT NOT NULL, PlateNo TEXT NOT NULL, UserName TEXT NOT NULL,
    PickupDate TEXT NOT NULL, DropOffDate TEXT NULL, Km TEXT NOT NULL, EmptyKm TEXT NOT NULL, TotalKm TEXT NOT NULL,
    WaitTimeMinutes TEXT NOT NULL, WaitCharge TEXT NOT NULL, Charge TEXT NOT NULL, RealCharge TEXT NOT NULL,
    TripId TEXT NOT NULL, FromPlaceName TEXT NOT NULL, ToPlaceName TEXT NOT NULL, DatePart TEXT NOT NULL,
    TimePart TEXT NOT NULL, VehicleDateKey TEXT NOT NULL, VehicleCode TEXT NOT NULL, CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AppTripCurrents_AreaCode_CreatedAt ON AppTripCurrents(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_AppTripCurrents_AreaCode_VehicleNo_PickupDate ON AppTripCurrents(AreaCode, VehicleNo, PickupDate);
CREATE INDEX IF NOT EXISTS IX_AppTripCurrents_AreaCode_PlateNo_PickupDate ON AppTripCurrents(AreaCode, PlateNo, PickupDate);

CREATE TABLE IF NOT EXISTS AppTripArchives (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, AreaCode TEXT NOT NULL, RowOrder INTEGER NOT NULL,
    GeneratedId TEXT NOT NULL, VehicleNo TEXT NOT NULL, PlateNo TEXT NOT NULL, UserName TEXT NOT NULL,
    PickupDate TEXT NOT NULL, DropOffDate TEXT NULL, Km TEXT NOT NULL, EmptyKm TEXT NOT NULL, TotalKm TEXT NOT NULL,
    WaitTimeMinutes TEXT NOT NULL, WaitCharge TEXT NOT NULL, Charge TEXT NOT NULL, RealCharge TEXT NOT NULL,
    TripId TEXT NOT NULL, FromPlaceName TEXT NOT NULL, ToPlaceName TEXT NOT NULL, DatePart TEXT NOT NULL,
    TimePart TEXT NOT NULL, VehicleDateKey TEXT NOT NULL, VehicleCode TEXT NOT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AppTripArchives_AreaCode_CreatedAt ON AppTripArchives(AreaCode, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_AppTripArchives_AreaCode_VehicleNo_PickupDate ON AppTripArchives(AreaCode, VehicleNo, PickupDate);
CREATE INDEX IF NOT EXISTS IX_AppTripArchives_AreaCode_PlateNo_PickupDate ON AppTripArchives(AreaCode, PlateNo, PickupDate);

CREATE TABLE IF NOT EXISTS AppManagedRecords (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL,
    ModuleKey TEXT NOT NULL,
    RowOrder INTEGER NOT NULL,
    DataJson TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    IsArchived INTEGER NOT NULL DEFAULT 0,
    SavedAt TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_AppManagedRecords_Area_Module_Archive_Order
ON AppManagedRecords(AreaCode, ModuleKey, IsArchived, RowOrder);
CREATE INDEX IF NOT EXISTS IX_AppManagedRecords_Area_Module_CreatedAt
ON AppManagedRecords(AreaCode, ModuleKey, CreatedAt);
" );

    // Nâng cấp database cũ: bổ sung cột ghép tài xế cho bảng cuốc mà không làm mất dữ liệu.
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverNames", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverEmployeeCodes", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverPhones", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "ShiftSoTai", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverCount", "INTEGER NOT NULL DEFAULT 0");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverMatchStatus", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverShiftStartAt", "TEXT NULL");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "DriverShiftNextAt", "TEXT NULL");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "AppUserName", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "AppVehicleNo", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "AppVehicleCode", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "AppTripId", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripCurrents", "AppMatchStatus", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverNames", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverEmployeeCodes", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverPhones", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "ShiftSoTai", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverCount", "INTEGER NOT NULL DEFAULT 0");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverMatchStatus", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverShiftStartAt", "TEXT NULL");
    await EnsureColumnAsync(db, "TaxiTripArchives", "DriverShiftNextAt", "TEXT NULL");
    await EnsureColumnAsync(db, "TaxiTripArchives", "AppUserName", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "AppVehicleNo", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "AppVehicleCode", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "AppTripId", "TEXT NOT NULL DEFAULT ''");
    await EnsureColumnAsync(db, "TaxiTripArchives", "AppMatchStatus", "TEXT NOT NULL DEFAULT ''");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrents_AreaCode_DriverShiftStartAt ON TaxiTripCurrents(AreaCode, DriverShiftStartAt);");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_TaxiTripArchives_AreaCode_DriverShiftStartAt ON TaxiTripArchives(AreaCode, DriverShiftStartAt);");

    // Dữ liệu của các phiên bản cũ chưa có khái niệm khu vực.
    // Migrate đúng MỘT LẦN sang Kho Bạc Liêu để giữ các ngày cũ.
    await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS AppMigrations (
    MigrationKey TEXT NOT NULL PRIMARY KEY,
    AppliedAt TEXT NOT NULL
);

INSERT INTO TaxiTripArchiveSyncs
    (LegacySyncId, AreaCode, AreaName, SourceUserName, FileId, ReportId,
     FromDate, ToDate, FromTime, ToTime, RowCount, CreatedAt, SavedAt)
SELECT s.Id, 'BL', 'Bạc Liêu', 'legacy', s.FileId, s.ReportId,
       s.FromDate, s.ToDate, s.FromTime, s.ToTime, s.RowCount, s.CreatedAt, s.CreatedAt
FROM TaxiTripSyncs s
WHERE NOT EXISTS (SELECT 1 FROM AppMigrations WHERE MigrationKey = 'LegacyTripsToBLArchiveV1')
  AND NOT EXISTS (SELECT 1 FROM TaxiTripArchiveSyncs a WHERE a.LegacySyncId = s.Id);

INSERT INTO TaxiTripArchives
    (SyncId, AreaCode, RowOrder, SoHieu, BienSo, BatDau, KetThuc,
     KmCoKhach, KmRong, TongKm, ThanhTien, DiemDau, DiemCuoi, CreatedAt, SavedAt)
SELECT a.Id, 'BL', t.RowOrder, t.SoHieu, t.BienSo, t.BatDau, t.KetThuc,
       t.KmCoKhach, t.KmRong, t.TongKm, t.ThanhTien, t.DiemDau, t.DiemCuoi,
       t.CreatedAt, t.CreatedAt
FROM TaxiTrips t
JOIN TaxiTripArchiveSyncs a ON a.LegacySyncId = t.SyncId
WHERE NOT EXISTS (SELECT 1 FROM AppMigrations WHERE MigrationKey = 'LegacyTripsToBLArchiveV1')
  AND NOT EXISTS (
      SELECT 1 FROM TaxiTripArchives x
      WHERE x.SyncId = a.Id AND x.RowOrder = t.RowOrder
  );

INSERT OR IGNORE INTO AppMigrations(MigrationKey, AppliedAt)
VALUES ('LegacyTripsToBLArchiveV1', datetime('now'));
" );
}

static async Task EnsureColumnAsync(LocalDbContext db, string tableName, string columnName, string definition)
{
    var connection = db.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync();

    await using var check = connection.CreateCommand();
    check.CommandText = $"PRAGMA table_info(\"{tableName}\")";
    await using var reader = await check.ExecuteReaderAsync();
    var exists = false;
    while (await reader.ReadAsync())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
        {
            exists = true;
            break;
        }
    }
    await reader.DisposeAsync();

    if (exists) return;
    await using var alter = connection.CreateCommand();
    alter.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {definition}";
    await alter.ExecuteNonQueryAsync();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
