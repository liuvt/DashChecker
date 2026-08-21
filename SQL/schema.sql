PRAGMA foreign_keys = ON;

-- ===== Tài khoản / khu vực =====
CREATE TABLE IF NOT EXISTS LocalCredentials (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NULL,
    ProtectedPassword TEXT NULL,
    RememberPassword INTEGER NOT NULL,
    DeviceId TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AreaAccounts (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL UNIQUE,
    AreaCode TEXT NOT NULL,
    AreaName TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    UpdatedAt TEXT NOT NULL
);

INSERT OR IGNORE INTO AreaAccounts(UserName, AreaCode, AreaName, IsActive, UpdatedAt)
VALUES ('namthangbl', 'BL', 'Bạc Liêu', 1, datetime('now'));
INSERT OR IGNORE INTO AreaAccounts(UserName, AreaCode, AreaName, IsActive, UpdatedAt)
VALUES ('namthangvl', 'VL', 'Vĩnh Long', 1, datetime('now'));
INSERT OR IGNORE INTO AreaAccounts(UserName, AreaCode, AreaName, IsActive, UpdatedAt)
VALUES ('namthangvinhlong', 'VL', 'Vĩnh Long', 1, datetime('now'));

-- ===== CUỐC XE CURRENT =====
CREATE TABLE IF NOT EXISTS TaxiTripCurrentSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, AreaName TEXT NOT NULL, SourceUserName TEXT NOT NULL,
    FileId TEXT NOT NULL, ReportId INTEGER NOT NULL,
    FromDate TEXT NOT NULL, ToDate TEXT NOT NULL, FromTime TEXT NOT NULL, ToTime TEXT NOT NULL,
    RowCount INTEGER NOT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NULL
);

CREATE TABLE IF NOT EXISTS TaxiTripCurrents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SyncId INTEGER NOT NULL, AreaCode TEXT NOT NULL, RowOrder INTEGER NOT NULL,
    SoHieu TEXT NOT NULL, BienSo TEXT NOT NULL, BatDau TEXT NOT NULL, KetThuc TEXT NOT NULL,
    KmCoKhach TEXT NOT NULL, KmRong TEXT NOT NULL, TongKm TEXT NOT NULL, ThanhTien TEXT NOT NULL,
    DiemDau TEXT NOT NULL, DiemCuoi TEXT NOT NULL,
    DriverNames TEXT NOT NULL DEFAULT '',
    DriverEmployeeCodes TEXT NOT NULL DEFAULT '',
    DriverPhones TEXT NOT NULL DEFAULT '',
    ShiftSoTai TEXT NOT NULL DEFAULT '',
    DriverCount INTEGER NOT NULL DEFAULT 0,
    DriverMatchStatus TEXT NOT NULL DEFAULT '',
    DriverShiftStartAt TEXT NULL,
    DriverShiftNextAt TEXT NULL,
    AppUserName TEXT NOT NULL DEFAULT '',
    AppVehicleNo TEXT NOT NULL DEFAULT '',
    AppVehicleCode TEXT NOT NULL DEFAULT '',
    AppTripId TEXT NOT NULL DEFAULT '',
    AppMatchStatus TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (SyncId) REFERENCES TaxiTripCurrentSyncs(Id) ON DELETE CASCADE
);

-- ===== CUỐC XE KHO =====
CREATE TABLE IF NOT EXISTS TaxiTripArchiveSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    LegacySyncId INTEGER NULL UNIQUE,
    AreaCode TEXT NOT NULL, AreaName TEXT NOT NULL, SourceUserName TEXT NOT NULL,
    FileId TEXT NOT NULL, ReportId INTEGER NOT NULL,
    FromDate TEXT NOT NULL, ToDate TEXT NOT NULL, FromTime TEXT NOT NULL, ToTime TEXT NOT NULL,
    RowCount INTEGER NOT NULL, CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TaxiTripArchives (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SyncId INTEGER NOT NULL, AreaCode TEXT NOT NULL, RowOrder INTEGER NOT NULL,
    SoHieu TEXT NOT NULL, BienSo TEXT NOT NULL, BatDau TEXT NOT NULL, KetThuc TEXT NOT NULL,
    KmCoKhach TEXT NOT NULL, KmRong TEXT NOT NULL, TongKm TEXT NOT NULL, ThanhTien TEXT NOT NULL,
    DiemDau TEXT NOT NULL, DiemCuoi TEXT NOT NULL,
    DriverNames TEXT NOT NULL DEFAULT '',
    DriverEmployeeCodes TEXT NOT NULL DEFAULT '',
    DriverPhones TEXT NOT NULL DEFAULT '',
    ShiftSoTai TEXT NOT NULL DEFAULT '',
    DriverCount INTEGER NOT NULL DEFAULT 0,
    DriverMatchStatus TEXT NOT NULL DEFAULT '',
    DriverShiftStartAt TEXT NULL,
    DriverShiftNextAt TEXT NULL,
    AppUserName TEXT NOT NULL DEFAULT '',
    AppVehicleNo TEXT NOT NULL DEFAULT '',
    AppVehicleCode TEXT NOT NULL DEFAULT '',
    AppTripId TEXT NOT NULL DEFAULT '',
    AppMatchStatus TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL,
    FOREIGN KEY (SyncId) REFERENCES TaxiTripArchiveSyncs(Id) ON DELETE CASCADE
);

-- ===== LÊN / XUỐNG CA CURRENT =====
CREATE TABLE IF NOT EXISTS ShiftCurrentSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, AreaName TEXT NOT NULL, SourceUserName TEXT NOT NULL,
    SpreadsheetId TEXT NOT NULL, SheetName TEXT NOT NULL,
    SourceDate TEXT NOT NULL, RowCount INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL, SavedAt TEXT NULL
);

CREATE TABLE IF NOT EXISTS ShiftCurrents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SyncId INTEGER NOT NULL, AreaCode TEXT NOT NULL, SourceRow INTEGER NOT NULL,
    SourceDate TEXT NOT NULL,
    SoTai TEXT NOT NULL, SoCho TEXT NOT NULL,
    BienKiemSoat TEXT NOT NULL, BienKiemSoatNormalized TEXT NOT NULL,
    HoTenMsnv TEXT NOT NULL, DriverName TEXT NOT NULL, EmployeeCode TEXT NOT NULL, DriverPhone TEXT NOT NULL,
    TrangThaiLenXuongCa TEXT NOT NULL,
    LoaiHinhHopTac TEXT NOT NULL, HinhThucKinhDoanh TEXT NOT NULL,
    LyDoXuongCa TEXT NOT NULL, GhiChu TEXT NOT NULL,
    SourceTime TEXT NOT NULL, SourceAt TEXT NOT NULL,
    HinhThucLuong TEXT NOT NULL, IsActive INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (SyncId) REFERENCES ShiftCurrentSyncs(Id) ON DELETE CASCADE
);

-- ===== LÊN / XUỐNG CA KHO =====
CREATE TABLE IF NOT EXISTS ShiftArchiveSyncs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL, AreaName TEXT NOT NULL, SourceUserName TEXT NOT NULL,
    SpreadsheetId TEXT NOT NULL, SheetName TEXT NOT NULL,
    SourceDate TEXT NOT NULL, RowCount INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL,
    UNIQUE(AreaCode, SourceDate)
);

CREATE TABLE IF NOT EXISTS ShiftArchives (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SyncId INTEGER NOT NULL, AreaCode TEXT NOT NULL, SourceRow INTEGER NOT NULL,
    SourceDate TEXT NOT NULL,
    SoTai TEXT NOT NULL, SoCho TEXT NOT NULL,
    BienKiemSoat TEXT NOT NULL, BienKiemSoatNormalized TEXT NOT NULL,
    HoTenMsnv TEXT NOT NULL, DriverName TEXT NOT NULL, EmployeeCode TEXT NOT NULL, DriverPhone TEXT NOT NULL,
    TrangThaiLenXuongCa TEXT NOT NULL,
    LoaiHinhHopTac TEXT NOT NULL, HinhThucKinhDoanh TEXT NOT NULL,
    LyDoXuongCa TEXT NOT NULL, GhiChu TEXT NOT NULL,
    SourceTime TEXT NOT NULL, SourceAt TEXT NOT NULL,
    HinhThucLuong TEXT NOT NULL, IsActive INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL, SavedAt TEXT NOT NULL,
    FOREIGN KEY (SyncId) REFERENCES ShiftArchiveSyncs(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_AreaCode_SourceDate ON ShiftCurrents(AreaCode, SourceDate);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_AreaCode_Plate ON ShiftCurrents(AreaCode, BienKiemSoatNormalized);
CREATE INDEX IF NOT EXISTS IX_ShiftCurrents_AreaCode_SoTai ON ShiftCurrents(AreaCode, SoTai);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_AreaCode_SourceDate ON ShiftArchives(AreaCode, SourceDate);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_AreaCode_Plate ON ShiftArchives(AreaCode, BienKiemSoatNormalized);
CREATE INDEX IF NOT EXISTS IX_ShiftArchives_AreaCode_SoTai ON ShiftArchives(AreaCode, SoTai);

-- Driver shift interval indexes
CREATE INDEX IF NOT EXISTS IX_TaxiTripCurrents_AreaCode_DriverShiftStartAt ON TaxiTripCurrents(AreaCode, DriverShiftStartAt);
CREATE INDEX IF NOT EXISTS IX_TaxiTripArchives_AreaCode_DriverShiftStartAt ON TaxiTripArchives(AreaCode, DriverShiftStartAt);

-- ================================================================
-- DATA.SKYSOFT.VN - XE ONLINE CURRENT / KHO
-- ================================================================
CREATE TABLE IF NOT EXISTS OnlineVehicleCurrents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL,
    VehicleId TEXT NOT NULL,
    PlateNo TEXT NOT NULL,
    VehicleNo TEXT NOT NULL,
    VehicleCode TEXT NOT NULL,
    GpsDate TEXT NULL,
    UpdateDate TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS OnlineVehicleArchives (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL,
    VehicleId TEXT NOT NULL,
    PlateNo TEXT NOT NULL,
    VehicleNo TEXT NOT NULL,
    VehicleCode TEXT NOT NULL,
    GpsDate TEXT NULL,
    UpdateDate TEXT NULL,
    CreatedAt TEXT NOT NULL,
    SavedAt TEXT NOT NULL
);

-- ================================================================
-- DATA.SKYSOFT.VN - BÁO CÁO CUỐC KHÁCH CURRENT / KHO
-- Flow: online_taxi -> query_taxi_trips 3 ngày -> lọc 05:00 -> km > 19.49
-- ================================================================
CREATE TABLE IF NOT EXISTS AppTripCurrents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL,
    RowOrder INTEGER NOT NULL,
    GeneratedId TEXT NOT NULL,
    VehicleNo TEXT NOT NULL,
    PlateNo TEXT NOT NULL,
    UserName TEXT NOT NULL,
    PickupDate TEXT NOT NULL,
    DropOffDate TEXT NULL,
    Km TEXT NOT NULL,
    EmptyKm TEXT NOT NULL,
    TotalKm TEXT NOT NULL,
    WaitTimeMinutes TEXT NOT NULL,
    WaitCharge TEXT NOT NULL,
    Charge TEXT NOT NULL,
    RealCharge TEXT NOT NULL,
    TripId TEXT NOT NULL,
    FromPlaceName TEXT NOT NULL,
    ToPlaceName TEXT NOT NULL,
    DatePart TEXT NOT NULL,
    TimePart TEXT NOT NULL,
    VehicleDateKey TEXT NOT NULL,
    VehicleCode TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AppTripArchives (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AreaCode TEXT NOT NULL,
    RowOrder INTEGER NOT NULL,
    GeneratedId TEXT NOT NULL,
    VehicleNo TEXT NOT NULL,
    PlateNo TEXT NOT NULL,
    UserName TEXT NOT NULL,
    PickupDate TEXT NOT NULL,
    DropOffDate TEXT NULL,
    Km TEXT NOT NULL,
    EmptyKm TEXT NOT NULL,
    TotalKm TEXT NOT NULL,
    WaitTimeMinutes TEXT NOT NULL,
    WaitCharge TEXT NOT NULL,
    Charge TEXT NOT NULL,
    RealCharge TEXT NOT NULL,
    TripId TEXT NOT NULL,
    FromPlaceName TEXT NOT NULL,
    ToPlaceName TEXT NOT NULL,
    DatePart TEXT NOT NULL,
    TimePart TEXT NOT NULL,
    VehicleDateKey TEXT NOT NULL,
    VehicleCode TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    SavedAt TEXT NOT NULL
);
