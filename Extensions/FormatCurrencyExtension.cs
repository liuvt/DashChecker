using System.Globalization;

namespace DashChecker.Extensions;

/// <summary>
/// Extension dùng chung để format tiền VND và ngày giờ hiển thị trên giao diện.
/// </summary>
public static class FormatCurrencyExtension
{
    // Convert string to string VND
    private static readonly string zero = "0";
    // Culture info for Vietnamese currency formatting
    private static readonly CultureInfo viCulture = new CultureInfo("vi-VN");

    // Phương thức trả về giá trị string
    public static string ltvVNDCurrency(this object input)
    {
        try
        {
            // Nếu đầu vào là null, trả về chuỗi "0"
            if (input == null) return zero;

            // Nếu đầu vào là số nguyên, định dạng nó
            if (input is decimal dec)
            {
                return dec.ToString("N0", viCulture);
            }

            // Nếu đầu vào là số nguyên 64-bit, định dạng nó
            if (input is string str)
            {
                // Định dạng lại chuỗi vào
                var clean = str.Replace(",", "").Replace(".", "").Trim();

                // Nếu chuỗi là số nguyên, định dạng nó
                if (decimal.TryParse(clean, out decimal parsed))
                {
                    // Nếu chuỗi là số nguyên, định dạng nó
                    return parsed.ToString("N0", viCulture);
                }
            }

            // Nếu đầu vào là số nguyên 32-bit, định dạng nó
            return zero;
        }
        catch
        {
            // Nếu có lỗi xảy ra, trả về chuỗi "0"
            return zero;
        }
    }

    // Phương thức trả về giá trị decimal
    public static decimal ltvVNDCurrencyToDecimal(this object input)
    {
        try
        {
            if (input == null)
                return 0;

            if (input is decimal dec)
                return dec;

            if (input is string str)
            {
                // Xoá đơn vị tiền tệ nếu có
                str = str.Replace("VND", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("₫", "")
                         .Replace(".", "")
                         .Replace(",", "")
                         .Trim();

                if (decimal.TryParse(str, out decimal parsed))
                    return parsed;
            }

            if (input is IConvertible)
                return Convert.ToDecimal(input);

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    /* Key test
     * "2025-09-17T01:07:00Z"	17/09/2025 08:07:00
        "2025-09-17T01:07:00+07:00"	17/09/2025 01:07:00
        new DateTime(2025,9,23,11,34,0) 23/09/2025 11:34:00
        "abc"	"abc" (nguyên gốc)
    */
    public static string ltvFormatISODateTime(this object date)
    {
        if (date == null) return "";

        if (date is DateTime d)
        {
            // Nếu là UTC => chuyển sang local (VN +7)
            if (d.Kind == DateTimeKind.Utc)
                d = d.ToLocalTime();
            // Nếu là Unspecified => coi như local, không đổi giờ
            else if (d.Kind == DateTimeKind.Unspecified)
                d = DateTime.SpecifyKind(d, DateTimeKind.Local);

            return d.ToString("dd/MM/yyyy HH:mm:ss");
        }

        if (date is string s)
        {
            if (DateTime.TryParse(
                    s,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                if (parsed.Kind == DateTimeKind.Utc)
                    parsed = parsed.ToLocalTime();
                else if (parsed.Kind == DateTimeKind.Unspecified)
                    parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);

                return parsed.ToString("dd/MM/yyyy HH:mm:ss");
            }

            return s; // không parse được thì giữ nguyên
        }

        return date.ToString();
    }
    /// <summary>
    /// Làm tròn số KM tối đa 2 chữ số thập phân và hiển thị theo định dạng Việt Nam.
    /// Ví dụ: 16,136 => 16,14; 16.136 => 16,14; 16 => 16.
    /// </summary>
    public static string ltvFormatKm(this object? input)
    {
        if (input == null) return "0";

        decimal value;

        try
        {
            if (input is string s)
            {
                s = s.Trim();
                if (string.IsNullOrWhiteSpace(s)) return "0";

                // Dữ liệu KM thường dùng dấu ',' hoặc '.' làm dấu thập phân.
                // Chuẩn hóa về dấu '.' để parse theo InvariantCulture.
                if (s.Contains(',') && !s.Contains('.'))
                    s = s.Replace(',', '.');

                if (!decimal.TryParse(
                        s,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    return "0";
                }
            }
            else if (input is IConvertible)
            {
                value = Convert.ToDecimal(input, CultureInfo.InvariantCulture);
            }
            else
            {
                return "0";
            }

            value = Math.Round(value, 2, MidpointRounding.AwayFromZero);

            // Tối đa 2 chữ số thập phân, dùng dấu phẩy theo vi-VN.
            return value.ToString("0.##", viCulture);
        }
        catch
        {
            return "0";
        }
    }

}