# TPL - VinaCAD Batch Plotter & PDF Editor

## Giới thiệu
**TPL** là plugin VinaCAD được phát triển bằng C#/.NET. Công cụ hỗ trợ in bản vẽ hàng loạt và quản lý, chỉnh sửa tệp PDF ngay trong VinaCAD.

---

## Các tính năng chính

- **In Hàng Loạt (Batch Plotting):** 
  - Tự động nhận diện khung bản vẽ và in hàng loạt Layout/Model chỉ với vài cú click.
  - Tối ưu hóa hiệu năng in ấn và quản lý các thiết lập máy in chuyên nghiệp.
- **Biên Tập PDF Tích Hợp (PDF Editor):**
  - Xem trước tài liệu PDF trực quan (sử dụng thư viện hiển thị tốc độ cao).
  - Ghép nhiều tệp PDF riêng lẻ thành một tệp duy nhất (Merge PDF).
  - Xoay trang, xóa trang, sắp xếp thứ tự các trang trực tiếp trên giao diện của plugin.
  - Hỗ trợ xuất PDF ra các định dạng hình ảnh chất lượng cao.
- **Giao Diện Hiện Đại (WPF Dark Theme):**
  - Giao diện được thiết kế hoàn toàn bằng WPF với ngôn ngữ thiết kế **Dark Mode** đồng bộ, hiện đại, mang lại trải nghiệm chuyên nghiệp cho người dùng.
  - Hỗ trợ đa ngôn ngữ (Localization).
  - Tích hợp trực tiếp thanh Ribbon của VinaCAD.
- **Quản Lý Bản Quyền (License Manager):**
  - Hệ thống xác thực thông tin thiết bị và quản lý kích hoạt bản quyền an toàn.

---

## Yêu cầu hệ thống

- **Hệ điều hành:** Windows 64-bit.
- **Phiên bản VinaCAD:** VinaCAD 2026.
- **Runtime:** .NET 8 Desktop Runtime.

---

## Danh sách lệnh trong VinaCAD

- `TPL`: Khởi chạy giao diện chính hỗ trợ in ấn hàng loạt và biên tập tệp PDF.
- `TPL_LICENSE`: Mở cửa sổ thông tin và kích hoạt bản quyền sản phẩm.

---

- **Cấu trúc Thư mục Dự án:**
  - `UI/`: Chứa toàn bộ giao diện người dùng WPF (`MainWindow`, `PdfEditorWindow`, `LicenseWindow`, `ProgressWindow`, `DarkProgressControl`).
  - `Logic/`: Chứa logic xử lý nghiệp vụ, in ấn bản vẽ và cấu hình (`CoreLogic.cs`, `PlotLogic.cs`, `LicenseManager.cs`, `Localization.cs`).
  - Root: Gồm các entry point chính (`Commands.cs`, `RibbonSetup.cs`), tệp dự án (`TPL.csproj`) và các tài nguyên đi kèm.
- **Công nghệ sử dụng:**
  - **WPF (Windows Presentation Foundation):** Thiết kế giao diện Dark Mode đồng bộ, chuyên nghiệp.
  - **Thư viện PDF:** `PdfiumViewer` và `PDFsharp` đảm nhận việc hiển thị và xử lý cấu trúc file PDF.
  - **VinaCAD API:** Sử dụng `Prima.VinaCAD` cho lớp ứng dụng/editor và `Teigha` cho database, geometry, runtime và PDF export.
  - **Quản lý Tài nguyên (Resource & Memory Management):** Tách biệt logic xử lý bản vẽ và quản lý bộ nhớ thông qua Transient Graphics để VinaCAD hoạt động ổn định.

---

## Hướng dẫn Build & Triển khai

1. **Chuẩn bị:** Cài VinaCAD 2026 và mở `TPL.sln` bằng Visual Studio.
2. **Build:** Thực hiện Build project ở chế độ mong muốn.
3. **Tự động Deploy:** Sự kiện post-build trong `TPL.csproj` sao chép bundle vào thư mục autoload của VinaCAD:
   ```text
   %AppData%\VinaCAD\ApplicationPlugins\TPL.bundle
   ```
4. **Khởi động:** Mở lại VinaCAD; plugin sẽ được nạp tự động. Nếu SDK được cài ở thư mục khác, build với `-p:VinaCadSdkDir="đường-dẫn-VinaCAD-2026"`.

---

## Tác giả
- **Tam Hoang**
