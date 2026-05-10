# TPL - AutoCAD Batch Plotter

## Giới thiệu
**TPL** (trước đây là NDPL) là một Plugin mạnh mẽ dành cho AutoCAD được viết bằng C# .NET. Công cụ này chuyên hỗ trợ quá trình in ấn hàng loạt (Batch Plotting) và quản lý, chỉnh sửa tệp PDF đầu ra một cách trực quan, hiệu quả.

## Các tính năng chính
- **In Hàng Loạt (Batch Plotting):** In nhiều bản vẽ, layout cùng lúc một cách nhanh chóng với giao diện hiện đại.
- **Xử lý PDF Tích Hợp:**
  - Gộp các bản in thành một file PDF duy nhất (Merge PDF).
  - Trình chỉnh sửa PDF tích hợp (PDF Editor): Xem trước, xoay trang, quản lý trang ngay trong ứng dụng mà không cần phần mềm bên thứ ba.
  - Chuyển đổi file PDF sang định dạng hình ảnh.
- **Giao Diện Thân Thiện (UI/UX):**
  - Được xây dựng trên nền tảng WPF mang lại trải nghiệm mượt mà.
  - Hỗ trợ thay đổi giao diện Sáng/Tối (Light/Dark Theme).
  - Đa ngôn ngữ (Localization).
  - Tích hợp trực tiếp vào thanh Ribbon của AutoCAD.
- **Quản Lý Bản Quyền:** Hệ thống cấp phép và kiểm tra bản quyền mạnh mẽ, an toàn.

## Môi trường & Yêu cầu hệ thống
- **Hệ điều hành:** Windows 64-bit
- **AutoCAD:** Hỗ trợ từ AutoCAD 2021 đến 2026 (Series R24.0 đến R25.1)
- **Framework:** .NET Framework 4.8

## Các lệnh trong AutoCAD
- `TPL`: Mở giao diện chính của chương trình (Batch Plotter & PDF Editor).
- `TPL_LICENSE`: Mở cửa sổ quản lý bản quyền phần mềm.

## Công nghệ sử dụng
- **AutoCAD .NET API:** Tương tác sâu với lõi của AutoCAD.
- **WPF (Windows Presentation Foundation):** Thiết kế giao diện người dùng chính (`MainWindow.xaml`, `PdfEditorWindow.xaml`).
- **PdfiumViewer & PDFsharp:** Xử lý hiển thị và biên tập file PDF.
- **Kiến trúc:** Phân tách logic lõi (`CoreLogic.cs`, `PlotLogic.cs`) và giao diện, quản lý tài nguyên (Transient Graphics) chặt chẽ tránh rò rỉ bộ nhớ.

## Hướng dẫn Build & Cài đặt
1. Mở Solution `TPL.sln` bằng Visual Studio.
2. Build project. Thuộc tính Post-build trong `TPL.csproj` sẽ tự động tạo gói `TPL.bundle` và copy vào thư mục plugins của AutoCAD (`%AppData%\Autodesk\ApplicationPlugins\TPL.bundle`).
3. Khởi động lại AutoCAD. Plugin sẽ tự động được tải (LoadReasons="OnAutoCADStartup").

## Tác giả
- **Tam Hoang**
