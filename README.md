# TPL - AutoCAD Batch Plotter & PDF Editor

## Giới thiệu
**TPL** là một AutoCAD Plugin mạnh mẽ được phát triển bằng ngôn ngữ C# .NET. Công cụ này hỗ trợ các kỹ sư và kiến trúc sư tối ưu hóa quy trình in ấn bản vẽ hàng loạt (Batch Plotting). PDF Editor được đóng gói thành ứng dụng Windows độc lập để việc xem và chỉnh sửa PDF không chiếm tài nguyên hoặc ảnh hưởng đến tiến trình AutoCAD.

---

## Các tính năng chính

- **In Hàng Loạt (Batch Plotting):** 
  - Tự động nhận diện khung bản vẽ và in hàng loạt Layout/Model chỉ với vài cú click.
  - Tối ưu hóa hiệu năng in ấn và quản lý các thiết lập máy in chuyên nghiệp.
  - Hỗ trợ chuyển PDF kết xuất sang PNG/JPG chất lượng cao.
- **Ứng Dụng PDF Editor Độc Lập:**
  - TPL tự động chuyển danh sách PDF vừa kết xuất sang `TPL.PdfEditor.exe`; ứng dụng không tham chiếu AutoCAD API.
  - Màn hình `PDF tools` điều hướng đến workspace riêng cho Merge, Split, Edit, Viewer, Rotate, Remove, Extract và Rearrange; mỗi workspace chỉ hiển thị công cụ phù hợp với tác vụ đã chọn.
  - Xem trước tài liệu PDF trực quan (sử dụng thư viện hiển thị tốc độ cao).
  - Mở, nhập và kéo-thả nhiều tệp PDF; ghép thành một tài liệu duy nhất.
  - Xoay trái/phải, nhân bản, xóa, kéo-thả và sắp xếp nhiều trang.
  - Undo/Redo, Save/Save As, trích xuất các trang đã chọn và các phím tắt chuẩn.
- **Giao Diện Hiện Đại (WPF Dark Theme):**
  - Giao diện được thiết kế hoàn toàn bằng WPF với ngôn ngữ thiết kế **Dark Mode** đồng bộ, hiện đại, mang lại trải nghiệm chuyên nghiệp cho người dùng.
  - Hỗ trợ đa ngôn ngữ (Localization).
  - Tích hợp trực tiếp thanh Ribbon tiện dụng trên AutoCAD.
- **Quản Lý Bản Quyền (License Manager):**
  - Hệ thống xác thực thông tin thiết bị và quản lý kích hoạt bản quyền an toàn.

---

## Yêu cầu hệ thống

- **Hệ điều hành:** Windows 64-bit.
- **Phiên bản AutoCAD:** Hỗ trợ từ AutoCAD 2021 đến 2026 (Series R24.0 đến R25.1).
- **Runtime:** .NET Framework 4.8.

---

## Danh sách lệnh trong AutoCAD

- `TPL`: Khởi chạy giao diện chính hỗ trợ in ấn hàng loạt và biên tập tệp PDF.
- `TPL_LICENSE`: Mở cửa sổ thông tin và kích hoạt bản quyền sản phẩm.

---

- **Cấu trúc Thư mục Dự án:**
  - `UI/`: Chứa giao diện WPF dùng bởi plugin và source giao diện được link vào PDF Editor.
  - `PdfEditor/`: Project `TPL.PdfEditor` tạo ứng dụng Windows x64 độc lập, không tham chiếu Autodesk assemblies.
  - `Logic/`: Chứa logic xử lý nghiệp vụ, in ấn bản vẽ và cấu hình (`CoreLogic.cs`, `PlotLogic.cs`, `LicenseManager.cs`, `Localization.cs`).
  - Root: Gồm các entry point chính (`Commands.cs`, `RibbonSetup.cs`), tệp dự án (`TPL.csproj`) và các tài nguyên đi kèm.
- **Công nghệ sử dụng:**
  - **WPF (Windows Presentation Foundation):** Thiết kế giao diện Dark Mode đồng bộ, chuyên nghiệp.
  - **Thư viện PDF:** `PdfiumViewer` và `PDFsharp` đảm nhận việc hiển thị và xử lý cấu trúc file PDF.
  - **Quản lý Tài nguyên (Resource & Memory Management):** Tách biệt logic xử lý bản vẽ và quản lý bộ nhớ thông qua Transient Graphics để đảm bảo AutoCAD hoạt động ổn định, không bị rò rỉ bộ nhớ hoặc crash hệ thống.

---

## Hướng dẫn Build & Triển khai

1. **Chuẩn bị:** Mở tệp Solution `TPL.sln` bằng Visual Studio.
2. **Build:** Thực hiện Build project ở chế độ mong muốn.
3. **Tự động Deploy:** Sự kiện Post-build được cấu hình sẵn trong `TPL.csproj` sẽ build PDF Editor, tạo bundle và sao chép plugin cùng ứng dụng độc lập vào thư mục Plugins của AutoCAD:
   ```text
   %AppData%\Autodesk\ApplicationPlugins\TPL.bundle
   ```
4. **Khởi động:** Mở AutoCAD lên và Plugin sẽ tự động được tải (LoadOnAutoCADStartup).
5. **Mở độc lập:** Chạy `Contents\PdfEditor\TPL.PdfEditor.exe`, dùng shortcut Start Menu từ installer, hoặc kéo tệp PDF vào cửa sổ ứng dụng.

---

## Tác giả
- **Tam Hoang**
