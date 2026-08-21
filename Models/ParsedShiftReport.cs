namespace DashChecker.Models;

public sealed record ParsedShiftAssignment(
    int SourceRow,
    DateTime SourceDate,
    string SoTai,
    string SoCho,
    string BienKiemSoat,
    string BienKiemSoatNormalized,
    string HoTenMsnv,
    string DriverName,
    string EmployeeCode,
    string DriverPhone,
    string TrangThaiLenXuongCa,
    string LoaiHinhHopTac,
    string HinhThucKinhDoanh,
    string LyDoXuongCa,
    string GhiChu,
    TimeSpan SourceTime,
    DateTime SourceAt,
    string HinhThucLuong,
    bool IsActive);

public sealed record ParsedShiftReport(
    string SpreadsheetId,
    string SheetName,
    DateTime SourceDate,
    IReadOnlyList<ParsedShiftAssignment> Rows);
