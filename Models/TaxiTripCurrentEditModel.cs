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

    /// <summary>
    /// Chuỗi ngày giờ chuẩn 24h "dd/MM/yyyy HH:mm:ss" (không dùng AM/PM).
    /// </summary>
    public string BatDauText
    {
        get => BatDau.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        set
        {
            var parsed = value.ltvStringToDateTime(CultureInfo.InvariantCulture);
            if (parsed.HasValue)
                BatDau = parsed.Value;
        }
    }

    /// <summary>
    /// Chuỗi ngày giờ chuẩn 24h "dd/MM/yyyy HH:mm:ss" (không dùng AM/PM).
    /// </summary>
    public string KetThucText
    {
        get => KetThuc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        set
        {
            var parsed = value.ltvStringToDateTime(CultureInfo.InvariantCulture);
            if (parsed.HasValue)
                KetThuc = parsed.Value;
        }
    }

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
    /// Map đúng 10 cột Google Sheet Cuốc Xe Đồng Hồ (Thô): A..J.
    /// A Số hiệu, B Biển số, C Bắt đầu, D Kết thúc, E KM có khách,
    /// F KM rỗng, G Tổng KM, H Thành tiền, I Điểm đầu, J Điểm cuối.
    /// </summary>
    public IReadOnlyList<object?> ToGoogleSheetValues() => new object?[]
    {
        SoHieu,
        BienSo,
        BatDau.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        KetThuc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        Math.Round(KmCoKhach, 2, MidpointRounding.AwayFromZero).ltvFormatKm(),
        Math.Round(KmRong, 2, MidpointRounding.AwayFromZero).ltvFormatKm(),
        Math.Round(TongKm, 2, MidpointRounding.AwayFromZero).ltvFormatKm(),
        ThanhTien,
        DiemDau,
        DiemCuoi
    };
}
