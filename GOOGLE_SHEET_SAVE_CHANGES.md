# Lưu GoogleSheet - BÁO CÁO ĐỒNG HỒ

## Mapping Current -> Google Sheet F:O

| Cột | TaxiTripCurrentEditModel |
|---|---|
| F | SoHieu |
| G | BienSo |
| H | BatDau (`dd/MM/yyyy HH:mm:ss`) |
| I | KetThuc (`dd/MM/yyyy HH:mm:ss`) |
| J | KmCoKhach |
| K | KmRong |
| L | TongKm |
| M | ThanhTien |
| N | DiemDau |
| O | DiemCuoi |

## UI
- Nút `Lưu GoogleSheet` nằm ngay cạnh `Chốt & Lưu Kho` tại trang Báo cáo đồng hồ.
- Nút lấy toàn bộ Current của khu vực, không phụ thuộc paging/search đang hiển thị.
- Dữ liệu được map qua `TaxiTripCurrentEditModel.From(row).ToGoogleSheetValues()`.

## Google Sheet
- SpreadsheetId: `1txl9y-tck1EwE3PKVbMIROQWZ__OK09RuJuCnpK-9q0`
- Sheet: `BÁO CÁO ĐỒNG HỒ`
- Range CRUD: `F2:O`
- Create: ghi theo lô bên dưới dòng cuối đang dùng; không tái sử dụng dòng trống ở giữa.
- Update: chỉ ghi `F{row}:O{row}`.
- Delete: chỉ clear `F{row}:O{row}`, không xóa physical row.
- Nếu thiếu row ở cuối grid, service chỉ append thêm row trống ở cuối Sheet.

## Credential
`GgsCheckerServer` dùng lại cấu hình Service Account từ `GoogleShift`:
- `GoogleShift:ServiceAccountJsonPath`, hoặc
- `GoogleShift:ServiceAccountJson`.

Fallback: `ltvggsheetaccount.json` trong thư mục project/app.
