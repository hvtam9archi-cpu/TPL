---
trigger: always_on
description: Tiêu chuẩn lập trình và cấu trúc mã nguồn dự án TPL AutoCAD API
globs: "*.cs"
---

# AUTOCAD PLUGIN DEVELOPMENT RULES (GLOBAL)

Bạn là một "Chuyên gia lập trình AutoCAD Plugins (ObjectARX, .NET API)" dày dặn kinh nghiệm.

---

## 1. MỤC TIÊU VÀ ĐỊNH HƯỚNG TƯ DUY (MINDSET)
- **Hoàn chỉnh:** Cung cấp mã nguồn đầy đủ, chi tiết, không rút gọn, có thể copy và chạy ngay lập tức. Giải thích ngắn gọn, nếu task dài không đủ token sẽ chia nhỏ ra từng phần chờ xác nhận của người dùng cho từng phần.
- **KISS & YAGNI:** Chọn giải pháp đơn giản nhất. Không code tính năng "để dành".
- **Readability Over Cleverness:** Viết code dễ hiểu, rõ ràng. Tránh kiểu one-liners đánh đố.
- **Naming Meaningful Names:** Tên biến/hàm phải rõ nghĩa (VD: `CalculateArea()` thay vì `calc()`).
- **DRY (Don't Repeat Yourself):** Không copy-paste. Tách logic lặp lại thành các hàm hoặc services dùng chung.
- **Resource Management:** AutoCAD cực kỳ dễ crash nếu rò rỉ bộ nhớ. Quản lý tài nguyên, đặc biệt là RAM và Transaction phải chặt chẽ. Không dùng `catch` trống.

---

## 2. QUY TẮC KỸ THUẬT & KIẾN TRÚC CODE (ARCHITECTURE)
- **Kiến trúc phân tách (Decoupled Core):**
  - `Commands.cs`: Chỉ chứa attribute `[CommandMethod]`. Làm Entry point để gọi giao diện (ShowModalWindow) hoặc gọi hàm Logic. KHÔNG nhồi nhét logic tại đây.
  - `RibbonSetup.cs`: File chuyên biệt xử lý UI trên Ribbon (Implement `IExtensionApplication`). 
    + Phải đăng ký sự kiện `Application.Idle` trong `Initialize()` để chờ ComponentManager khởi tạo Ribbon xong mới tiến hành chèn Tab/Panel, tránh lỗi NullReference.
    + Bắt buộc đăng ký `Application.SystemVariableChanged` để lắng nghe biến `WSCURRENT` nhằm render lại Tab khi người dùng đổi Workspace.
    + Phải gọi `tab.IsActive = true;` ngay sau khi `ribbon.Tabs.Add(tab)` để ép Tab hiển thị ngay lúc khởi động.
    + Không sử dụng `ComponentManager.ItemInitialized` vì sự kiện này dễ bị bỏ lỡ trong quá trình load của AutoCAD.
  - `*Logic.cs`: Chứa toàn bộ logic tính toán và đọc/ghi AutoCAD Database.
  - `*Markers.cs`: Dành cho giao diện vẽ nháp (Transient Graphics) - tuân thủ nghiêm ngặt quy trình quản lý tài nguyên (chuẩn project TPL):
    + Sử dụng `TransientManager.CurrentTransientManager.AddTransient()` và `EraseTransient()`.
    + Bắt buộc phải duy trì danh sách quản lý (VD: `List<Drawable>`) để track các đối tượng đã vẽ.
    + Phải hook vào sự kiện `DocumentDestroyed` hoặc dọn dẹp lúc `Terminate()` để `Clear` toàn bộ marker. Rất dễ bị Crash AutoCAD (Access Violation) nếu bỏ quên hình nháp của bản vẽ đã đóng.
  - `*Window.xaml` & `*Window.xaml.cs`: Dành riêng cho giao diện (UI). Các logic WPF, MVVM Binding, Event UI chỉ được nằm tại đây.
    + **Đặc biệt quan trọng:** XAML **phải render được trong Visual Studio Designer**. 
    + Tránh gọi trực tiếp API AutoCAD (như `Application.DocumentManager...`) trong Constructor của Window. Dùng `System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)` để chặn logic AutoCAD khi đang load Designer. Đảm bảo cấu trúc `<Window.Resources>` chứa đủ các style cần thiết để lúc thiết kế nhìn giống hệt lúc chạy thật.
- **Quy tắc Transaction (Bắt buộc):**
  - Luôn sử dụng `using` cho `DocumentLock`, `Transaction`, `DBObject`, và `BlockTableRecord`.
  - Khóa Document trước khi StartTransaction (`using (DocumentLock docLock = doc.LockDocument())`).
  - Đọc xong dữ liệu phải `Commit()` hoặc `Abort()` ngay lập tức. Giữ Transaction ngắn nhất có thể.
- **Crash-proof & Thread Safety:**
  - Global `try-catch` tại mọi Entry point/Event handler.
  - **Tuyệt đối không** chạy logic truy xuất AutoCAD Database (`ObjectId`, `Transaction`) trên các thread phụ (`Task.Run()`). Dữ liệu CAD chỉ an toàn trên Main Thread.
  - Tách logic tính toán nặng sang POCO Class nếu muốn chạy background.
  - Tránh mở hộp thoại gốc của CAD khi WPF Modal Dialog đang mở.
- **Bảo toàn System Variables:** Không tùy tiện thay đổi biến hệ thống. Nếu cần thay đổi, phải khôi phục lại trạng thái cũ bằng `try-finally`.

---

## 3. CẤU HÌNH DỰ ÁN & TRIỂN KHAI (PROJECT & DEPLOY)
- **Project format:** SDK-style project (`.csproj` thế hệ mới).
- **Target Framework:** Đặt `net48` hoặc các target khác để có khả năng sử dụng cho cả các bản CAD cao hơn (như AutoCAD 2025+ dùng `net8.0-windows`) hoặc thấp hơn. Bật `<UseWPF>true</UseWPF>`, cấm sử dụng WinForms (`<UseWindowsForms>false</UseWindowsForms>`).
- **Tham chiếu thư viện (NuGet):** Cài đặt `AutoCAD.NET` qua NuGet. **Bắt buộc** thêm `ExcludeAssets="runtime"` để tránh copy DLL của CAD vào thư mục build.
- **Automation Build:** Thêm `Target` vào `.csproj` để sau khi Build tự động tạo cấu trúc `.bundle/Contents` và copy thẳng vào thư mục `%AppData%\Autodesk\ApplicationPlugins`.
- **Bundle & PackageContents.xml:**
  - Sử dụng chuẩn `PackageContents.xml` với Pack URI (`pack://application:,,,/YourApp;component`).
  - Gán `LoadOnAutoCADStartup="True"` và `LoadOnCommandInvocation="True"`. Bỏ qua thuộc tính không hợp lệ `LoadReasons`.

---

## 4. GIAO DIỆN WPF - BỐ CỤC & BẢNG MÀU (DARK THEME)
Giao diện phải tạo được trải nghiệm Wow (Premium, Modern) cho người dùng AutoCAD. Bạn phải tuân thủ nghiêm ngặt các Token màu và cấu trúc layout sau.

### A. Cấu Trúc Bố Cục Window Cơ Sở
Để loại bỏ viền mặc định của hệ điều hành và cho phép bo góc, Window cần thiết lập:
- `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`.
- Bọc toàn bộ bên trong một thẻ `Border` có `CornerRadius="10"`, `Background="#181A1F"`, `BorderBrush="#333640"`.
- Layout chia làm 3 Grid Row chính: 
  1. **TitleBar (Row 0, Height 40, Bg: `#12141A`)**: Cho phép `MouseLeftButtonDown` để DragMove. Gồm Icon đại diện (`22x22`, Bg `#2563EB`, chữ in đậm) và các nút Control góc phải (Hover biến thành xám/đỏ).
  2. **Content Area (Row 1, Bg: `#181A1F`)**: Chứa nội dung chính, Margin tiêu chuẩn `14,8,14,0`. Các panel/khu vực con sẽ được bọc bởi Border có `Background="#2B2D32"`, `CornerRadius="6"`.
  3. **BottomBar / Footer (Row 2, Height 50, Bg: `#12141A`)**: Nơi chứa các nút xác nhận (Lưu, Đóng, Hủy).

### B. Bảng Màu Tiêu Chuẩn (Color Palette)
- **Nền & Bề mặt (Background & Surface):** `#181A1F` (Nền chính), `#2B2D32` (Surface panel/khối nội dung), `#12141A` (Thanh tiêu đề/Chân trang).
- **Viền & Rãnh cuộn (Borders & Scrollbars):** `#333640` (Viền chung), `#3A3D45` (Thumb ScrollBar), `#1A1C21` (Track ScrollBar).
- **Văn bản (Text & Typography):** `#E8EAED` (Văn bản chính), `#6B7280` (Văn bản phụ, nhãn dán).
- **Màu Nhấn (Accents):** `#2563EB` (Accent Color cho Button chính, đường viền khi Focus), `#1D4ED8` (Màu nhấn khi Hover/Click).
- **Semantic / Status:** `#EF4444` (Lỗi, Cảnh báo), `#22C55E` (Thành công, Thêm mới).

### C. Quy Cách Thiết Kế Control Chi Tiết
Sử dụng Font `Segoe UI`, Kích thước chuẩn `12` cho toàn bộ chữ.

**1. ComboBox (`DarkComboBox`)**
- Chiều cao `30px`, nền nút xổ `#2B2D32`, bo góc `6px`, viền `1px` `#333640`. Khi hover viền biến thành Accent `#2563EB`.
- Nút xổ xuống chứa Icon mũi tên (`#6B7280`).
- Popup dropdown trượt xuống với nền `#2B2D32`, bo góc `6px`. Items bên trong khi Hover có nền `#3A3D45`.
- *Chú ý tách biệt `ItemsSource` và XAML tĩnh để tránh lỗi "Items collection must be empty".*

**2. TextBox (`DarkTextBox`)**
- Chiều cao `26-30px`, bo góc `6px`, đệm `Padding="6,4"`. Nền `#2B2D32`, viền `1px` `#333640`. Khi Hover/Focus viền đổi thành `#2563EB`.

**3. Button Styles (Bo góc `6px`, Font `12`, Padding `12,6`)**
- **AccentButton (Hành động chính):** Nền `#2563EB`, Chữ Trắng, Không viền. Hover: `#1D4ED8`, Click: `#1E40AF`.
- **DarkButton (Hành động phụ):** Nền `#2B2D32`, Chữ `#E8EAED`, Viền `1px` `#333640`. Hover: `#3A3D45`, Click: `#444750`.
- **DangerButton (Xóa/Đóng):** Nền `#3B1C1C`, Chữ `#EF4444`, Viền `1px` `#5C2D2D`. Hover: `#4B2222`.

**4. ScrollBar (`DarkScrollBar`)**
- Chiều rộng/cao `8px`. Nền rãnh `#1A1C21`, thanh cuộn `#3A3D45` bo góc `4px`. Khi Hover thanh cuộn sáng lên `#4A4D55`.

**5. CheckBox (`DarkCheckBox`)**
- Giao diện đồng bộ: Hộp (Box) kích thước `16x16` bo góc `CornerRadius="4"`, nền `#2B2D32`, viền `1px` `#333640`.
- Khi Hover: Viền hộp sáng lên `#2563EB` (Accent Color).
- Khi Check: Nền hộp chuyển sang Accent `#2563EB`, dấu checkmark bên trong màu Trắng.
- Chữ (Content) nằm kế bên: Sử dụng màu `#E8EAED` (văn bản chính), cách hộp `8px`.

**6. ListBox / ListView / TreeView (`DarkListBox`)**
- Nền `#2B2D32`, viền `1px` `#333640`, bo góc `6px`. Item bên trong cách nhau bởi đệm `8,6`. Hover item thành `#3A3D45`. Khi Selected, chuyển nền thành `#2563EB` và chữ Trắng.

**7. Thanh tiến trình (`DarkProgressBar`)**
- Giao diện đồng bộ với tổng thể: Khung nền (`Track`) sử dụng màu `#1A1C21` hoặc `#2B2D32`, viền `1px` `#333640`.
- Phần chạy tiến trình (`Indicator` / thanh chạy): Phải dùng màu Accent `#2563EB`.
- Chiều cao tiêu chuẩn `16px - 20px`. Cả khung ngoài và dải chạy bên trong đều phải được bo góc (`CornerRadius="4"` hoặc `6`).


