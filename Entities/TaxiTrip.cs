namespace DashChecker.Entities;

public sealed class TaxiTrip
{
    public long Id { get; set; }
    public long SyncId { get; set; }
    public TaxiTripSync? Sync { get; set; }

    public int RowOrder { get; set; }
    public string SoHieu { get; set; } = string.Empty;
    public string BienSo { get; set; } = string.Empty;
    public DateTime BatDau { get; set; }
    public DateTime KetThuc { get; set; }
    public decimal KmCoKhach { get; set; }
    public decimal KmRong { get; set; }
    public decimal TongKm { get; set; }
    public decimal ThanhTien { get; set; }
    public string DiemDau { get; set; } = string.Empty;
    public string DiemCuoi { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
