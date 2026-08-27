using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace DashChecker.Extensions;

/// <summary>
/// Các hàm mở rộng dùng chung khi đọc/ghi Google Sheets và chuyển đổi dữ liệu trả về từ Sheets API.
/// </summary>
public static class GoogleSheetExtension
{
    #region CRUD Google Sheets API

    /// <summary>
    /// Lấy toàn bộ dữ liệu trong một range từ Google Sheets.
    /// </summary>
    public static async Task<IList<IList<object>>> ltvGetSheetValuesAsync(
        this SheetsService service,
        string spreadsheetId,
        string range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(range);

        var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
        var response = await request.ExecuteAsync(cancellationToken);
        return response.Values ?? new List<IList<object>>();
    }

    /// <summary>
    /// Cập nhật dữ liệu vào vùng chỉ định. Dữ liệu được gửi ở chế độ USER_ENTERED
    /// để Google Sheets tự xử lý số, ngày tháng, công thức... giống như nhập trực tiếp trên giao diện.
    /// </summary>
    public static async Task<IList<IList<object>>> UpdateSheetValuesAsync(
        this SheetsService service,
        string spreadsheetId,
        string range,
        ValueRange valueRange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(valueRange);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(range);

        var updateRequest = service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
        updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        updateRequest.IncludeValuesInResponse = true;

        var response = await updateRequest.ExecuteAsync(cancellationToken);
        return response.UpdatedData?.Values ?? new List<IList<object>>();
    }

    /// <summary>
    /// Cập nhật nhiều range trong một request. Phù hợp cập nhật một dòng theo map header -> cột.
    /// </summary>
    public static async Task ltvBatchUpdateSheetValuesAsync(
        this SheetsService service,
        string spreadsheetId,
        IEnumerable<ValueRange> data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

        var ranges = data.ToList();
        if (ranges.Count == 0)
            return;

        var body = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED",
            Data = ranges
        };

        var request = service.Spreadsheets.Values.BatchUpdate(body, spreadsheetId);
        await request.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Xóa nội dung trong một vùng nhưng không xóa vật lý hàng/cột.
    /// </summary>
    public static async Task ltvClearSheetValuesAsync(
        this SheetsService service,
        string spreadsheetId,
        string range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(range);

        var clearRequest = service.Spreadsheets.Values.Clear(new ClearValuesRequest(), spreadsheetId, range);
        await clearRequest.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Xóa vật lý một dòng khỏi Google Sheet.
    /// rowIndex là index 0-based theo API Google Sheets: 0 = dòng 1 trên giao diện.
    /// </summary>
    public static async Task DeleteDimensionRequestAsync(
        this SheetsService service,
        string spreadsheetId,
        string sheetName,
        int rowIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        // Không cho xóa dòng tiêu đề (index 0) hoặc index âm.
        if (rowIndex <= 0)
            throw new InvalidOperationException("Không thể xóa dòng tiêu đề hoặc rowIndex không hợp lệ.");

        var getSpreadsheet = service.Spreadsheets.Get(spreadsheetId);
        getSpreadsheet.Fields = "sheets.properties(sheetId,title)";
        var spreadsheet = await getSpreadsheet.ExecuteAsync(cancellationToken);

        var sheet = spreadsheet.Sheets?.FirstOrDefault(s =>
            string.Equals(s.Properties?.Title, sheetName, StringComparison.OrdinalIgnoreCase));

        if (sheet?.Properties?.SheetId is not int sheetId)
            throw new InvalidOperationException($"Sheet với tên '{sheetName}' không tồn tại.");

        var deleteRequest = new Request
        {
            DeleteDimension = new DeleteDimensionRequest
            {
                Range = new DimensionRange
                {
                    SheetId = sheetId,
                    Dimension = "ROWS",
                    StartIndex = rowIndex,
                    EndIndex = rowIndex + 1
                }
            }
        };

        var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request> { deleteRequest }
        };

        var request = service.Spreadsheets.BatchUpdate(batchUpdateRequest, spreadsheetId);
        await request.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Thêm dữ liệu mới vào cuối bảng.
    /// </summary>
    public static async Task ltvAppendSheetValuesAsync(
        this SheetsService service,
        string spreadsheetId,
        string range,
        ValueRange valueRange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(valueRange);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(range);

        var appendRequest = service.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        appendRequest.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await appendRequest.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Thêm dữ liệu mới vào cuối bảng và trả về số dòng vừa được thêm theo kiểu 1-based của giao diện Sheets.
    /// Trả về 0 nếu Google không trả UpdatedRange.
    /// </summary>
    public static async Task<int> ltvAppendSheetValuesAndGetRowAsync(
        this SheetsService service,
        string spreadsheetId,
        string range,
        ValueRange valueRange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(valueRange);

        var appendRequest = service.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        appendRequest.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;

        var response = await appendRequest.ExecuteAsync(cancellationToken);
        return ltvGetRowNumberFromUpdatedRange(response.Updates?.UpdatedRange);
    }

    #endregion

    #region Đọc/chuẩn hóa dữ liệu Google Sheets

    /// <summary>
    /// Trích xuất giá trị theo index, trả về chuỗi mặc định nếu ô không tồn tại/rỗng.
    /// </summary>
    public static string ltvGetValueString(this IList<object>? item, int index, string defaultValue = "")
    {
        if (item is null || index < 0 || item.Count <= index || item[index] is null)
            return defaultValue;

        return item[index]?.ToString()?.Trim() ?? defaultValue;
    }

    /// <summary>
    /// Trích xuất số decimal theo index. Phù hợp dữ liệu tiền VND dạng 1.234.567 hoặc 1,234,567.
    /// </summary>
    public static decimal ltvGetValueDecimal(this IList<object>? item, int index, decimal defaultValue = 0m)
    {
        if (item is null || index < 0 || item.Count <= index || item[index] is null)
            return defaultValue;

        var raw = item[index];
        if (raw is decimal decimalValue)
            return decimalValue;

        if (raw is byte or sbyte or short or ushort or int or uint or long or ulong)
            return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);

        if (raw is float or double)
            return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);

        var text = raw.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;

        // Google Sheets có thể trả về số dạng invariant hoặc chuỗi đã format theo locale.
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var direct))
            return direct;

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("vi-VN"), out direct))
            return direct;

        var clean = text
            .Replace("VND", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₫", "")
            .Replace(".", "")
            .Replace(",", "")
            .Trim();

        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    /// <summary>
    /// Chuyển chuỗi ngày/giờ theo nhiều format phổ biến thành DateTime?.
    /// </summary>
    public static DateTime? ltvStringToDateTime(this string? input, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        culture ??= CultureInfo.InvariantCulture;

        string[] formats =
        [
            "HH:mm:ss dd/MM/yyyy",
            "HH:mm dd/MM/yyyy",
            "H:mm:ss dd/MM/yyyy",
            "H:mm dd/MM/yyyy",
            "HH:mm:ss - dd/MM/yyyy",
            "HH:mm - dd/MM/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm",
            "d/M/yyyy H:mm:ss",
            "d/M/yyyy H:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd",
            "dd/MM/yyyy",
            "d/M/yyyy",
            "MM/dd/yyyy"
        ];

        if (DateTime.TryParseExact(input.Trim(), formats, culture, DateTimeStyles.AllowWhiteSpaces, out var result))
            return result;

        if (DateTime.TryParse(input.Trim(), culture, DateTimeStyles.AllowWhiteSpaces, out result))
            return result;

        return null;
    }

    /// <summary>
    /// Chuyển chuỗi HH:mm:ss, HH:mm, H:mm:ss hoặc H:mm thành TimeSpan?.
    /// </summary>
    public static TimeSpan? ltvStringToTimeSpan(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string[] formats = [@"hh\:mm\:ss", @"hh\:mm", @"h\:mm\:ss", @"h\:mm"];
        if (TimeSpan.TryParseExact(input.Trim(), formats, CultureInfo.InvariantCulture, out var result))
            return result;

        return TimeSpan.TryParse(input.Trim(), CultureInfo.InvariantCulture, out result)
            ? result
            : null;
    }

    /// <summary>
    /// Chuyển "HH:mm:ss - dd/MM/yyyy" thành "dd/MM/yyyy HH:mm:ss".
    /// </summary>
    public static string? ltvStringToStringDateTime(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        const string format = "HH:mm:ss - dd/MM/yyyy";
        return DateTime.TryParseExact(input.Trim(), format, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var result)
            ? result.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Cắt ký tự V ở cuối biển số nếu có và trim khoảng trắng.
    /// </summary>
    public static string ltvNormalizePlate(this string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return string.Empty;

        var value = plate.Trim();
        return value.EndsWith("V", StringComparison.OrdinalIgnoreCase)
            ? value[..^1].TrimEnd()
            : value;
    }

    /// <summary>
    /// Convert giá trị Google Sheets thành boolean. Giá trị không nhận diện được trả false.
    /// </summary>
    public static bool ltvStringToBoolean(this IList<object>? item, int index)
    {
        if (item is null || index < 0 || item.Count <= index || item[index] is null)
            return false;

        var value = item[index]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" or "on" or "có" or "co" => true,
            _ => false
        };
    }

    /// <summary>
    /// Lấy số dòng 1-based từ UpdatedRange, ví dụ: 'Sheet1'!A25:M25 => 25.
    /// </summary>
    public static int ltvGetRowNumberFromUpdatedRange(string? updatedRange)
    {
        if (string.IsNullOrWhiteSpace(updatedRange))
            return 0;

        var bangIndex = updatedRange.LastIndexOf('!');
        var rangePart = bangIndex >= 0 ? updatedRange[(bangIndex + 1)..] : updatedRange;
        var firstCell = rangePart.Split(':', 2)[0];
        var digits = new string(firstCell.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var row) ? row : 0;
    }

    #endregion
}
