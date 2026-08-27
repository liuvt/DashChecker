# GgsDashChecker config fix

## Vấn đề
`appsettings.json` có section `GgsDashChecker`, nhưng project cũ chỉ bind `GoogleShift` vào `GoogleShiftOptions`. `GgsCheckerServer` cũng đang nhận `IOptions<GoogleShiftOptions>`, vì vậy credential trong `GgsDashChecker` không được đọc.

## Sửa
1. Thêm `Models/GgsDashCheckerOptions.cs`.
2. Bind `GgsDashChecker` trong `Program.cs`.
3. `GgsCheckerServer` nhận `IOptions<GgsDashCheckerOptions>`.
4. SpreadsheetId/SheetName lấy từ appsettings thay vì constant hard-code.
5. `.gitignore` chặn `App_Data/ggs-dashchecker-api.json`.

Cấu hình appsettings dùng:

```json
"GgsDashChecker": {
  "SpreadsheetId": "1txl9y-tck1EwE3PKVbMIROQWZ__OK09RuJuCnpK-9q0",
  "SheetName": "BÁO CÁO ĐỒNG HỒ",
  "ServiceAccountJsonPath": "App_Data/ggs-dashchecker-api.json",
  "ServiceAccountJson": "",
  "ApplicationName": "DashChecker - GgsDashChecker"
}
```
