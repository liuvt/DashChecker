using DashChecker.Extensions;
using DashChecker.Servers.Interfaces;
using DashChecker.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;

namespace DashChecker.Servers;

/// <summary>
/// CRUD Google Sheet "BÁO CÁO ĐỒNG HỒ".
/// Chỉ thao tác vùng F2:O của spreadsheet được cấu hình cố định bên dưới.
/// </summary>
public sealed class GgsCheckerServer : IGgsCheckerServer, IDisposable
{
    private const int StartRow = 2;
    private const int ColumnCount = 10;
    private const string StartColumn = "F";
    private const string EndColumn = "O";


    private readonly SheetsService _sheetsService;
    private readonly GgsDashCheckerOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public GgsCheckerServer(
        IOptions<GgsDashCheckerOptions> ggsOptions,
        IWebHostEnvironment environment)
    {
        _options = ggsOptions.Value;
        ValidateOptions(_options);

        var credential = LoadCredential(_options, environment)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = string.IsNullOrWhiteSpace(_options.ApplicationName)
                ? "DashChecker - GgsDashChecker"
                : _options.ApplicationName
        });
    }

    public async Task<List<GgsCheckerRow>> GetsAsync(
        CancellationToken cancellationToken = default)
    {
        var range = $"'{EscapeSheetName(_options.SheetName)}'!{StartColumn}{StartRow}:{EndColumn}";
        var values = await _sheetsService.ltvGetSheetValuesAsync(
            _options.SpreadsheetId, range, cancellationToken);

        var result = new List<GgsCheckerRow>();
        for (var index = 0; index < values.Count; index++)
        {
            var row = values[index];
            if (!HasAnyValue(row))
                continue;

            result.Add(new GgsCheckerRow
            {
                RowNumber = StartRow + index,
                Values = NormalizeValues(row)
            });
        }

        return result;
    }

    public async Task<int> AddAsync(
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken = default)
    {
        var result = await AddRangeAsync(
            new List<IReadOnlyList<object?>> { values },
            cancellationToken);
        return result.StartRow;
    }

    public async Task<GgsCheckerAppendResult> AddRangeAsync(
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return new GgsCheckerAppendResult(0, 0, 0);

        foreach (var row in rows)
            ValidateInput(row);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var targetRow = await GetNextAppendRowAsync(cancellationToken);
            var endRow = targetRow + rows.Count - 1;
            await EnsureRowCapacityAsync(endRow, cancellationToken);

            // Không ghi đè nếu trong lúc xác định vị trí có dữ liệu/công thức mới phát sinh.
            while (await RangeHasDataAsync(targetRow, endRow, cancellationToken))
            {
                targetRow = await GetNextAppendRowAsync(cancellationToken);
                endRow = targetRow + rows.Count - 1;
                await EnsureRowCapacityAsync(endRow, cancellationToken);
            }

            var values = rows
                .Select(row => (IList<object>)NormalizeValues(row))
                .ToList();

            var body = new ValueRange { Values = values };
            var range = $"'{EscapeSheetName(_options.SheetName)}'!{StartColumn}{targetRow}:{EndColumn}{endRow}";

            await _sheetsService.UpdateSheetValuesAsync(
                _options.SpreadsheetId, range, body, cancellationToken);

            return new GgsCheckerAppendResult(targetRow, endRow, rows.Count);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> UpdateAsync(
        int rowNumber,
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken = default)
    {
        ValidateRowNumber(rowNumber);
        ValidateInput(values);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!await RowHasDataAsync(rowNumber, cancellationToken))
                return false;

            var body = new ValueRange
            {
                Values = new List<IList<object>> { NormalizeValues(values) }
            };

            var range = $"'{EscapeSheetName(_options.SheetName)}'!{StartColumn}{rowNumber}:{EndColumn}{rowNumber}";
            await _sheetsService.UpdateSheetValuesAsync(
                _options.SpreadsheetId, range, body, cancellationToken);

            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        int rowNumber,
        CancellationToken cancellationToken = default)
    {
        ValidateRowNumber(rowNumber);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!await RowHasDataAsync(rowNumber, cancellationToken))
                return false;

            var range = $"'{EscapeSheetName(_options.SheetName)}'!{StartColumn}{rowNumber}:{EndColumn}{rowNumber}";
            await _sheetsService.ltvClearSheetValuesAsync(
                _options.SpreadsheetId, range, cancellationToken);

            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task EnsureRowCapacityAsync(
        int requiredLastRow,
        CancellationToken cancellationToken)
    {
        var get = _sheetsService.Spreadsheets.Get(_options.SpreadsheetId);
        get.Fields = "sheets.properties(sheetId,title,gridProperties.rowCount)";
        var spreadsheet = await get.ExecuteAsync(cancellationToken);

        var sheet = spreadsheet.Sheets?.FirstOrDefault(x =>
            string.Equals(x.Properties?.Title, _options.SheetName, StringComparison.OrdinalIgnoreCase));

        if (sheet?.Properties?.SheetId is not int sheetId)
            throw new InvalidOperationException($"Không tìm thấy Sheet '{_options.SheetName}'.");

        var currentRowCount = sheet.Properties.GridProperties?.RowCount ?? 0;
        if (requiredLastRow <= currentRowCount)
            return;

        var body = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new()
                {
                    AppendDimension = new AppendDimensionRequest
                    {
                        SheetId = sheetId,
                        Dimension = "ROWS",
                        Length = requiredLastRow - currentRowCount
                    }
                }
            }
        };

        await _sheetsService.Spreadsheets.BatchUpdate(body, _options.SpreadsheetId)
            .ExecuteAsync(cancellationToken);
    }

    private async Task<int> GetNextAppendRowAsync(CancellationToken cancellationToken)
    {
        var range = $"'{EscapeSheetName(_options.SheetName)}'!{StartColumn}{StartRow}:{EndColumn}";
        var values = await GetValuesIncludingFormulasAsync(range, cancellationToken);

        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (HasAnyValue(values[index]))
                return StartRow + index + 1;
        }

        return StartRow;
    }

    private async Task<bool> RowHasDataAsync(
        int rowNumber,
        CancellationToken cancellationToken)
        => await RangeHasDataAsync(rowNumber, rowNumber, cancellationToken);

    private async Task<bool> RangeHasDataAsync(
        int startRow,
        int endRow,
        CancellationToken cancellationToken)
    {
        var range = $"'{EscapeSheetName(_options.SheetName)}'!{StartColumn}{startRow}:{EndColumn}{endRow}";
        var values = await GetValuesIncludingFormulasAsync(range, cancellationToken);
        return values.Any(HasAnyValue);
    }

    private async Task<IList<IList<object>>> GetValuesIncludingFormulasAsync(
        string range,
        CancellationToken cancellationToken)
    {
        var request = _sheetsService.Spreadsheets.Values.Get(_options.SpreadsheetId, range);
        request.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMULA;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Values ?? new List<IList<object>>();
    }

    private static void ValidateOptions(GgsDashCheckerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SpreadsheetId))
            throw new InvalidOperationException("Chưa cấu hình GgsDashChecker:SpreadsheetId.");

        if (string.IsNullOrWhiteSpace(options.SheetName))
            throw new InvalidOperationException("Chưa cấu hình GgsDashChecker:SheetName.");
    }

    private static GoogleCredential LoadCredential(
        GgsDashCheckerOptions options,
        IWebHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
            return GoogleCredential.FromJson(options.ServiceAccountJson);

        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath))
        {
            var configuredPath = options.ServiceAccountJsonPath.Trim();
            var path = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));

            if (File.Exists(path))
                return GoogleCredential.FromFile(path);

            throw new FileNotFoundException(
                $"Không tìm thấy Google Service Account JSON tại: {path}", path);
        }

        // Fallback theo project mẫu Servers.zip.
        var legacyNames = new[]
        {
            Path.Combine(environment.ContentRootPath, "ltvggsheetaccount.json"),
            Path.Combine(AppContext.BaseDirectory, "ltvggsheetaccount.json")
        };

        var legacyPath = legacyNames.FirstOrDefault(File.Exists);
        if (legacyPath is not null)
            return GoogleCredential.FromFile(legacyPath);

        throw new InvalidOperationException(
            "Chưa cấu hình Service Account cho Google Sheet. " +
            "Hãy cấu hình GgsDashChecker:ServiceAccountJsonPath/ServiceAccountJson " +
            "hoặc đặt file ltvggsheetaccount.json trong thư mục ứng dụng.");
    }

    private static bool HasAnyValue(IList<object>? row)
        => row is not null && row.Any(value =>
            value is not null && !string.IsNullOrWhiteSpace(value.ToString()));

    private static List<object> NormalizeValues(IEnumerable<object?> values)
    {
        var normalized = values
            .Take(ColumnCount)
            .Select(value => value ?? string.Empty)
            .Cast<object>()
            .ToList();

        while (normalized.Count < ColumnCount)
            normalized.Add(string.Empty);

        return normalized;
    }

    private static void ValidateInput(IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != ColumnCount)
        {
            throw new ArgumentException(
                $"BÁO CÁO ĐỒNG HỒ yêu cầu đúng {ColumnCount} giá trị tương ứng F:O.",
                nameof(values));
        }
    }

    private static void ValidateRowNumber(int rowNumber)
    {
        if (rowNumber < StartRow)
            throw new ArgumentOutOfRangeException(nameof(rowNumber), "Chỉ được thao tác từ dòng 2 trở xuống.");
    }

    private static string EscapeSheetName(string name) => name.Replace("'", "''");

    public void Dispose()
    {
        _writeLock.Dispose();
        _sheetsService.Dispose();
    }
}
