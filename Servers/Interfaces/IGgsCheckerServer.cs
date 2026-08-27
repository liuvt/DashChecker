namespace DashChecker.Servers.Interfaces;

/// <summary>
/// CRUD dữ liệu Google Sheet "BÁO CÁO ĐỒNG HỒ" trên vùng F2:O.
/// RowNumber là số dòng thật trên Google Sheet (1-based như giao diện Google Sheets).
/// </summary>
public interface IGgsCheckerServer
{
    Task<List<GgsCheckerRow>> GetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm một dòng mới bên dưới dòng cuối đang có dữ liệu trong F:O.
    /// Không tái sử dụng lỗ trống ở giữa và không ghi đè dữ liệu cũ.
    /// </summary>
    Task<int> AddAsync(
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm nhiều dòng liên tiếp bằng một lần ghi Google API.
    /// Trả về dòng bắt đầu và dòng kết thúc vừa ghi.
    /// </summary>
    Task<GgsCheckerAppendResult> AddRangeAsync(
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sửa đúng vùng F:O tại một dòng cụ thể.
    /// </summary>
    Task<bool> UpdateAsync(
        int rowNumber,
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear F:O của dòng, không xóa physical row của Google Sheet.
    /// </summary>
    Task<bool> DeleteAsync(
        int rowNumber,
        CancellationToken cancellationToken = default);
}

public sealed class GgsCheckerRow
{
    public int RowNumber { get; init; }
    public List<object> Values { get; init; } = new();
}

public sealed record GgsCheckerAppendResult(
    int StartRow,
    int EndRow,
    int Count);
