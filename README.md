# TPL - AutoCAD Batch Plotter & PDF Editor

## Giới thiệu
**TPL** là một AutoCAD Plugin mạnh mẽ được phát triển bằng ngôn ngữ C# .NET. Công cụ này hỗ trợ các kỹ sư và kiến trúc sư tối ưu hóa quy trình in ấn bản vẽ hàng loạt (Batch Plotting) và quản lý, chỉnh sửa tệp PDF đầu ra một cách trực quan, nhanh chóng ngay trong môi trường AutoCAD.

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

## Kiến trúc mã nguồn & Công nghệ sử dụng

- **AutoCAD .NET API:** Tương tác trực tiếp với cơ sở dữ liệu AutoCAD và điều khiển tiến trình in ấn.
- **WPF (Windows Presentation Foundation):** Xây dựng giao diện đồ họa cao cấp (`MainWindow.xaml`, `PdfEditorWindow.xaml`, `LicenseWindow.xaml`).
- **Thư viện PDF chuyên dụng:** `PdfiumViewer` và `PDFsharp` đảm nhận việc dựng hình ảnh và thao tác cấu trúc tệp PDF.
- **Quản lý Tài nguyên (Resource & Memory Management):** Tách biệt logic xử lý bản vẽ (`PlotLogic.cs`, `CoreLogic.cs`) và quản lý bộ nhớ thông qua Transient Graphics để đảm bảo AutoCAD hoạt động ổn định, không bị rò rỉ bộ nhớ hoặc crash hệ thống.

---

## Hướng dẫn Build & Triển khai

1. **Chuẩn bị:** Mở tệp Solution `TPL.sln` bằng Visual Studio.
2. **Build:** Thực hiện Build project ở chế độ mong muốn.
3. **Tự động Deploy:** Sự kiện Post-build được cấu hình sẵn trong `TPL.csproj` sẽ tự động tạo thư mục bundle và sao chép toàn bộ DLL cần thiết cùng tệp cấu hình vào thư mục Plugins của AutoCAD:
   ```text
   %AppData%\Autodesk\ApplicationPlugins\TPL.bundle
   ```
4. **Khởi động:** Mở AutoCAD lên và Plugin sẽ tự động được tải (LoadOnAutoCADStartup).

---

## Tác giả
- **Tam Hoang**
