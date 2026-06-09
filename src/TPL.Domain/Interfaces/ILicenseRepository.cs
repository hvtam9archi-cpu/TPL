using TPL.Domain.Models;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Đọc/ghi thông tin bản quyền.
	/// Implementation: LicenseRepository (Infrastructure layer — Registry, Network).
	/// </summary>
	public interface ILicenseRepository
	{
		/// <summary>Đọc license từ storage (Registry).</summary>
		LicenseInfo GetLicenseInfo();

		/// <summary>Lưu license vào storage.</summary>
		void SaveLicenseInfo(LicenseInfo info);

		/// <summary>Cập nhật ngày chạy cuối.</summary>
		void UpdateLastRunDate(LicenseInfo info);

		/// <summary>Kích hoạt license bằng key.</summary>
		bool ActivateLicense(string activationKey, out string message);

		/// <summary>Lấy Hardware ID của máy.</summary>
		string GetHardwareId();

		/// <summary>Kiểm tra revoke list từ remote (fire-and-forget).</summary>
		void CheckRemoteRevokeAsync();

		/// <summary>Tạo key (admin only).</summary>
		string GenerateKey(string hwId, int days, string seq = "");
	}
}
