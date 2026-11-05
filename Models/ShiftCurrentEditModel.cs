using System.Globalization;
using DashChecker.Extensions;

namespace DashChecker.Models;

public sealed class ShiftCurrentEditModel
{
    // Dùng kiểu mạnh để Blazor .NET 9 bind đúng với <input type="date">.
    public DateTime SourceDate { get; set; } = DateTime.Today;

    public string SoTai { get; set; } = string.Empty;
    public string SoCho { get; set; } = "5 chỗ";
    public string BienKiemSoat { get; set; } = string.Empty;
    public string HoTenMsnv { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;
    public string TrangThaiLenXuongCa { get; set; } = "Lên ca";
    public string LoaiHinhHopTac { get; set; } = string.Empty;
    public string HinhThucKinhDoanh { get; set; } = string.Empty;
    public string LyDoXuongCa { get; set; } = string.Empty;
    public string GhiChu { get; set; } = string.Empty;

    public TimeOnly? SourceTime { get; set; } = new TimeOnly(5, 0, 0);

    /// <summary>
    /// Chuỗi giờ chuẩn 24h HH:mm:ss (không dùng AM/PM) để bind trực tiếp trên giao diện.
    /// </summary>
    public string SourceTimeText
    {
        get => (SourceTime ?? new TimeOnly(5, 0, 0)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        set
        {
            var ts = value.ltvStringToTimeSpan();
            if (ts.HasValue)
            {
                SourceTime = new TimeOnly(
                    Math.Clamp(ts.Value.Hours, 0, 23),
                    Math.Clamp(ts.Value.Minutes, 0, 59),
                    Math.Clamp(ts.Value.Seconds, 0, 59));
            }
        }
    }

    public string HinhThucLuong { get; set; } = string.Empty;

    public static ShiftCurrentEditModel CreateNew(DateTime sourceDate) => new()
    {
        SourceDate = sourceDate.Date,
        SourceTime = new TimeOnly(5, 0, 0),
        TrangThaiLenXuongCa = "Lên ca",
        SoCho = "5 chỗ"
    };

    public static ShiftCurrentEditModel From(ShiftListItem row) => new()
    {
        SourceDate = row.SourceDate.Date,
        SoTai = row.SoTai,
        SoCho = row.SoCho,
        BienKiemSoat = row.BienKiemSoat,
        HoTenMsnv = row.HoTenMsnv,
        DriverPhone = row.DriverPhone,
        TrangThaiLenXuongCa = row.TrangThaiLenXuongCa,
        LoaiHinhHopTac = row.LoaiHinhHopTac,
        HinhThucKinhDoanh = row.HinhThucKinhDoanh,
        LyDoXuongCa = row.LyDoXuongCa,
        GhiChu = row.GhiChu,
        SourceTime = TimeOnly.FromTimeSpan(row.SourceTime),
        HinhThucLuong = row.HinhThucLuong
    };
}
