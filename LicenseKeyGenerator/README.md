# TPL License Key Generator — Google Apps Script

Công cụ sinh mã kích hoạt cho hệ thống License của TPL.

## Cách triển khai (Deploy)

### 1. Tạo Apps Script project

1. Truy cập https://script.google.com/
2. Nhấn **+ New project**
3. Đặt tên project: `TPL License Key Generator`

### 2. Copy code

**File `Code.gs`**:
- Xoá nội dung mặc định trong `Code.gs`
- Copy toàn bộ nội dung từ `Code.gs` trong thư mục này vào

**File `Index.html`**:
- Vào menu **File → New → HTML file**
- Đặt tên là `Index`
- Copy toàn bộ nội dung từ `Index.html` vào

### 3. Cấu hình SecretKey (quan trọng!)

Trong `Code.gs`, biến `CONFIG.SECRET_KEY` phải **GIỐNG HỆT** với `SecretKey` trong `LicenseManager.cs`:

```csharp
// LicenseManager.cs
private const string SecretKey = "TPL_V1_SECRET_KEY_2026_NEVER_SHARE_THIS_EVER!!";
```

```javascript
// Code.gs
SECRET_KEY: "TPL_V1_SECRET_KEY_2026_NEVER_SHARE_THIS_EVER!!"
```

> ⚠️ Nếu bạn đổi SecretKey trong source C#, nhớ đổi cả ở đây!

### 4. Cấp quyền Script Properties

Script này dùng `PropertiesService.getScriptProperties()` để lưu danh sách thu hồi.
Khi deploy lần đầu, bạn cần cấp quyền:
- Vào **Project Settings** → check **Show "appsscript" manifest file in editor**
- Hoặc đơn giản: khi chạy thử lần đầu, Google sẽ tự yêu cầu cấp quyền

### 5. Deploy web app

1. Nhấn **Deploy → New deployment**
2. Chọn type: **Web app**
3. Cấu hình:
   - **Description**: `TPL License Key Generator v1.0`
   - **Execute as**: `Me` (chính bạn — tài khoản admin)
   - **Who has access**: `Anyone` (hoặc giới hạn nếu muốn)
4. Nhấn **Deploy**
5. **Copy URL** của web app để dùng

### 6. Cập nhật RevokeListUrl trong LicenseManager.cs

Sau khi deploy, bạn có **2 cách** để cập nhật URL cho C# client:

**Cách 1 (khuyên dùng):** Dùng URL của Apps Script
1. Mở Web App → footer → **📋 Copy Revoke URL**
2. Dán vào `LicenseManager.cs`:
   ```csharp
   public const string RevokeListUrl = "https://script.google.com/macros/s/.../exec?action=getRevokeCsv";
   ```

**Cách 2 (giữ nguyên Google Sheet cũ):**
- Vẫn dùng Google Sheet CSV như cũ, nhưng phải thao tác thủ công

### 7. Sử dụng

#### 🔑 Sinh mã kích hoạt
1. Mở URL web app → tab **Sinh mã**
2. Nhập **Hardware ID** (từ License Window của TPL → nút Copy)
3. Nhập **Số ngày** (≥ 9999 = Vĩnh viễn)
4. Nhập **Sequence** (tuỳ chọn, để phân biệt nhiều key cho cùng HWID)
5. Nhấn **Sinh mã kích hoạt**
6. Copy mã và gửi cho người dùng

#### ⛔ Quản lý thu hồi (Revoke)
1. Mở URL web app → tab **Thu hồi**
2. Chọn loại: **Hardware ID** hoặc **License Key**
3. Nhập giá trị cần thu hồi
4. Nhập lý do (tuỳ chọn)
5. Nhấn **Thêm vào danh sách thu hồi**
6. Client C# sẽ tự động kiểm tra khi chạy lệnh `TPL`
7. Nếu HWID hoặc Key nằm trong danh sách → license bị vô hiệu hoá ngay lập tức

**Danh sách thu hồi:**
- Hiển thị đầy đủ: loại, giá trị, lý do, ngày giờ
- Có thể xoá từng mục (✕) hoặc **Xoá tất cả**
- Nhấn **Làm mới** để cập nhật

## Cấu trúc mã kích hoạt

```
XXXX-XXXX-XXXX-XXXX (16 ký tự Base32)
```

| Offset | Bytes | Mô tả |
|--------|-------|-------|
| 0-1 | 2 | Số ngày (big-endian), ≥ 9999 = vĩnh viễn |
| 2-5 | 4 | 8 ký tự đầu của Hardware ID (hex → bytes) |
| 6 | 1 | Sequence byte (phân biệt nhiều key) |
| 7-9 | 3 | 3 byte đầu của SHA256(shortHwId\|days\|seqByte\|SecretKey) |

## Luồng thu hồi từ xa

```
Admin (Web App)                          C# Client (VinaCAD)
      │                                         │
      │  Thêm HWID/KEY vào Revoke List          │
      │─────────────────┐                       │
      │  PropertiesService                      │
      │◄────────────────┘                       │
      │                                         │
      │         User chạy lệnh TPL              │
      │                                         ├──→ GetLicenseInfo()
      │                                         │
      │         GET ?action=getRevokeCsv        │
      │◄────────────────────────────────────────┤
      │         Trả về CSV danh sách thu hồi     │
      ├────────────────────────────────────────→│
      │                                         ├──→ Contains(HWID)?
      │                                         ├──→ Contains(KEY)?
      │                                         │
      │         Nếu khớp → ExpirationDate=Min   │
      │                                         │
```

## Bảo mật

- **SecretKey** là điểm then chốt — giữ bí mật tuyệt đối
- Không share file `Code.gs` chứa SecretKey công khai
- Web App chạy dưới quyền của bạn (admin), người dùng không xem được code
- Nên giới hạn quyền truy cập Web App nếu cần
- Dữ liệu thu hồi lưu trong **Script Properties** của Google Apps Script (riêng tư, không public)
