# DashChecker (.NET 9.0.3)

## Chức năng

### 1. Quản lý cuốc xe
- Login SkySoft theo tài khoản.
- Mapping khu vực: `namthangbl -> BL`, `namthangvl/namthangvinhlong -> VL`.
- Current cuốc: SkySoft 05:00 hôm qua -> 05:00 hôm nay.
- Chỉ khi bấm **Lưu vào Kho** mới ghi snapshot lịch sử.
- Cột **Tài xế theo ca** được ghép từ dữ liệu QL_LEN_XUONG_CA theo Số tài + Biển số + ngày vận hành 05:00.
- Nếu một xe có 2/3 tài xế phù hợp, hệ thống giữ toàn bộ tên thay vì đoán một người.

### 2. Quản lý lên xuống ca
Nav: `/shift-management`

Nguồn mặc định:
- Spreadsheet ID: `169wRJ7BvJxH7bAuQwebYA16WGSKTYo0oJSM0gcwf5U0`
- Sheet: `QL_LEN_XUONG_CA`
- gid: `0`

Current ca lấy **ngày nguồn mới nhất** của đúng khu vực (BL hoặc VL). Khi bấm Cập nhật, chỉ Current bị thay thế. Khi bấm **Lưu vào Kho**, snapshot được lưu theo `AreaCode + SourceDate`; bấm lưu lại cùng ngày sẽ replace.

### Google Sheet access
DashChecker standalone đọc CSV export trực tiếp từ Google. File cần có quyền **Bất kỳ ai có đường liên kết - Người xem**. Nếu không muốn mở link công khai, thay `GoogleShiftService` bằng Google Sheets API + Service Account có quyền đọc file.

## SQLite
`App_Data/skysoft-trips.db`

Các bảng chính:
- `AreaAccounts`
- `TaxiTripCurrentSyncs`, `TaxiTripCurrents`
- `TaxiTripArchiveSyncs`, `TaxiTripArchives`
- `ShiftCurrentSyncs`, `ShiftCurrents`
- `ShiftArchiveSyncs`, `ShiftArchives`

## Chạy
```powershell
dotnet restore
dotnet build
dotnet run
```


## Current ca CRUD + ghép tài xế theo mốc giờ
- Current ca cho phép Thêm/Sửa/Xóa trực tiếp; cột Control chỉ hiện Sửa/Xóa khi xem; lúc thao tác sẽ hiện trạng thái Sửa/Thêm.
- Các thao tác Current không thay đổi Kho. Bấm Lưu vào Kho mới replace snapshot cùng AreaCode + ngày CreatedAt.
- Mốc ca dùng cửa sổ vận hành 05:00 -> 05:00. Thời gian ca < 05:00 được quy về ngày kế tiếp của SourceDate.
- Nếu một biển số có nhiều tài: tài có mốc Lên ca gần nhất nhưng không sau BatDau cuốc được gán cho cuốc; mốc kế tiếp là thời điểm kết thúc hiệu lực của tài trước.
- Bảng cuốc hiển thị thêm Mốc ca hiệu lực và lưu DriverShiftStartAt / DriverShiftNextAt.

## 3. Online App - data.skysoft.vn
Nav: `/online-app`

- `online_taxi`: lấy xe online và map `plateNo -> vehicleNo`.
- `query_taxi_trips`: lấy cuốc khách theo từng ngày API, sau đó lọc đúng cửa sổ vận hành `05:00 hôm qua -> 05:00 hôm nay`.
- Giữ điều kiện cuốc có `KM có khách > 19.49 km` giống Apps Script cũ.
- Khi lấy cuốc App, DashChecker tự ghép `App User + Số tài App + Trip ID` vào **Current Cuốc xe** theo biển số + thời gian bắt đầu (mặc định dung sai 15 phút). Nếu Current thay đổi sau ghép, trạng thái trở về **chưa chốt Kho**.
- BL/VL dùng bộ API riêng theo `AreaCode`. RG/CM/CT đã có slot cấu hình để mở rộng sau.

### Cấu hình Key/Token bằng User Secrets
Project đã có `UserSecretsId`; không lưu Key/Token thật trong `appsettings.json`.

```powershell
# Bạc Liêu
dotnet user-secrets set "SkySoftApp:Areas:BL:Key" "YOUR_BL_KEY"
dotnet user-secrets set "SkySoftApp:Areas:BL:Token" "YOUR_BL_TOKEN"

# Vĩnh Long
dotnet user-secrets set "SkySoftApp:Areas:VL:Key" "YOUR_VL_KEY"
dotnet user-secrets set "SkySoftApp:Areas:VL:Token" "YOUR_VL_TOKEN"
```

Nếu cần RG/CM/CT, dùng cùng mẫu với `RG`, `CM`, `CT`.

## Quy tắc Current / Kho cuốc xe
- Nhấn nav **Cuốc xe** luôn mở tab **Current**.
- **Cập nhật** chỉ thay Current trong ngày hiện hành.
- **Chốt & Lưu Kho** có dialog xác nhận ngày. Khi xác nhận, DashChecker xóa bản Kho của đúng `AreaCode + ngày CreatedAt` rồi copy toàn bộ Current vào Kho.
- Các ngày Kho cũ khác không bị tác động.

## Data SkySoft App tách riêng

Hai nghiệp vụ độc lập:

1. `/online-vehicles` - **Xe online**: Current / Kho.
2. `/app-trips` - **Báo cáo cuốc khách**: Current / Kho.

### Flow Báo cáo cuốc khách

Bám theo Apps Script `fetchAndProcessTrips`:

- gọi `online_taxi` trước để lấy `plateNo -> vehicleNo`;
- gọi `query_taxi_trips` đúng 3 ngày `i = 0, 1, 2`;
- ngày `i=0`: loại cuốc có `pickupDate > 05:00 hôm nay`;
- ngày `i=2`: loại cuốc có `dropOffDate < 05:00 hôm qua`;
- `km = round(rawKm / 1000, 2)` và chỉ giữ `km > 19.49`;
- cuốc không map được `vehicleNo` bị loại ở bước group-by giống Apps Script;
- group theo `vehicleNo`, sort số tài tự nhiên, trong mỗi xe sort theo `dropOffDate`;
- Current được replace khi bấm cập nhật; Kho chỉ replace theo ngày `CreatedAt` khi bấm `Chốt & Lưu Kho`.

Key/Token API không được ghi vào source. Dùng User Secrets, ví dụ:

```powershell
dotnet user-secrets set "SkySoftApp:Areas:BL:Key" "..."
dotnet user-secrets set "SkySoftApp:Areas:BL:Token" "..."
dotnet user-secrets set "SkySoftApp:Areas:VL:Key" "..."
dotnet user-secrets set "SkySoftApp:Areas:VL:Token" "..."
```


## MudBlazor

Project đã tích hợp MudBlazor 9.7.0 cho .NET 9.

- `DashChecker.csproj`: thêm package `MudBlazor`.
- `Program.cs`: đăng ký `AddMudServices()`.
- `Components/_Imports.razor`: thêm `@using MudBlazor`.
- `Components/App.razor`: nạp `MudBlazor.min.css` và `MudBlazor.min.js`.
- `Components/Layout/MainLayout.razor`: thêm `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`.

Sau khi giải nén/chuyển source, chạy:

```powershell
dotnet restore
dotnet build
dotnet watch
```

## Quản lý App (bổ sung)

Navigation `Quản lý App` gồm:

- App VNPay
- Khuyến mãi App Khách Hàng
- App Khách Hàng
- App Xanh SM
- Hợp đồng

Mỗi module được tách theo khu vực đăng nhập và có hai vùng `Current` / `Kho lưu trữ`.
Current hỗ trợ thêm, sửa, lưu và xóa theo kiểu spreadsheet; `Lưu vào Kho` replace riêng snapshot của đúng module/khu vực. Tất cả cột dữ liệu có thể bật/tắt bằng `Cột hiển thị`; `CreatedAt` do hệ thống tự tạo khi thêm dòng.

Dữ liệu 5 module dùng chung bảng SQLite `AppManagedRecords`, phân vùng bằng `AreaCode`, `ModuleKey` và `IsArchived` để việc bổ sung module mới sau này không phải nhân bản CRUD/database service.
