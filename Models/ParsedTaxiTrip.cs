namespace DashChecker.Models;

public sealed record ParsedTaxiTrip(
    string SoHieu,
    string BienSo,
    DateTime BatDau,
    DateTime KetThuc,
    decimal KmCoKhach,
    decimal KmRong,
    decimal TongKm,
    decimal ThanhTien,
    string DiemDau,
    string DiemCuoi);
