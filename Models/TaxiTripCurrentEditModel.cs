using DashChecker.Extensions;
using DashChecker.Services;
using System.Globalization;

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
    /// <summary>
    /// Map đúng 10 cột Google Sheet BÁO CÁO ĐỒNG HỒ: F..O.
    /// F Số hiệu, G Biển số, H Bắt đầu, I Kết thúc, J KM có khách,
    /// K KM rỗng, L Tổng KM, M Thành tiền, N Điểm đầu, O Điểm cuối.
    /// </summary>
    public IReadOnlyList<object?> ToGoogleSheetValues() => new object?[]
    {
        SoHieu,
        BienSo,
        BatDau.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        KetThuc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        KmCoKhach.ltvFormatKm(),
        KmRong.ltvFormatKm(),
        TongKm.ltvFormatKm(),
        ThanhTien,
        DiemDau,
        DiemCuoi
    };

}
