using DashChecker.Services;

namespace DashChecker.Models;

public sealed class TaxiTripCurrentEditModel
{
    public string SoHieu { get; set; } = string.Empty;
    public string BienSo { get; set; } = string.Empty;
    public DateTime BatDau { get; set; } = VietnamClock.Now.Date.AddHours(5);
    public DateTime KetThuc { get; set; } = VietnamClock.Now.Date.AddHours(5);
    public decimal KmCoKhach { get; set; }
    public decimal KmRong { get; set; }
    public decimal TongKm { get; set; }
    public decimal ThanhTien { get; set; }
    public string DiemDau { get; set; } = string.Empty;
    public string DiemCuoi { get; set; } = string.Empty;

    public static TaxiTripCurrentEditModel CreateNew() => new()
    {
        BatDau = VietnamClock.Now.Date.AddHours(5),
        KetThuc = VietnamClock.Now.Date.AddHours(5)
    };

    public static TaxiTripCurrentEditModel From(TaxiTripListItem row) => new()
    {
        SoHieu = row.SoHieu,
        BienSo = row.BienSo,
        BatDau = row.BatDau,
        KetThuc = row.KetThuc,
        KmCoKhach = row.KmCoKhach,
        KmRong = row.KmRong,
        TongKm = row.TongKm,
        ThanhTien = row.ThanhTien,
        DiemDau = row.DiemDau,
        DiemCuoi = row.DiemCuoi
    };
}
