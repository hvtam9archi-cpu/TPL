using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;

namespace TPL.Presentation.ViewModels
{
	/// <summary>
	/// ViewModel cho LicenseWindow.
	/// Hiển thị trạng thái license, cho phép nhập key và copy HWID.
	/// </summary>
	public partial class LicenseWindowViewModel : ObservableObject
	{
		private readonly ILicenseRepository _licenseRepo;
		private readonly ILocalizationService _l10n;

		public LicenseWindowViewModel(ILicenseRepository licenseRepo, ILocalizationService l10n)
		{
			_licenseRepo = licenseRepo;
			_l10n = l10n;
		}

		// ─── Properties ──────────────────────────────────────────────────

		[ObservableProperty]
		private readonly string hardwareId = "";

		[ObservableProperty]
		private readonly string statusMessage = "";

		[ObservableProperty]
		private readonly string statusColor = "#E8EAED";

		[ObservableProperty]
		private readonly string activationKey = "";

		[ObservableProperty]
		private readonly string expirationDisplay = "";

		[ObservableProperty]
		private readonly bool isLicenseValid;

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly string activationMessage = "";

		// ═══════════════════════════════════════════════════════════════════

		/// <summary>Khởi tạo license info — gọi từ Window.Loaded.</summary>
=======
		private readonly string activationMessage = "";

		// ═══════════════════════════════════════════════════════════════════

		/// <summary>Khởi tạo license info — gọi từ Window.Loaded.</summary>
>>>>>>> After
		private readonly string activationMessage = "";

		// ═══════════════════════════════════════════════════════════════════

		/// <summary>Khởi tạo license info — gọi từ Window.Loaded.</summary>
		public void LoadLicenseInfo()
		{
			HardwareId = _licenseRepo.GetHardwareId();
			RefreshStatus();
		}

		/// <summary>Cập nhật trạng thái hiển thị.</summary>
		private void RefreshStatus()
		{
			LicenseInfo info = _licenseRepo.GetLicenseInfo();
			IsLicenseValid = info.IsValid;

			if (info.IsHardwareChanged)
			{
				StatusMessage = _l10n.Translate("lic_hw_changed");
				StatusColor = "#EF4444";
			}
			else if (info.ExpirationDate == DateTime.MaxValue)
			{
				StatusMessage = _l10n.Translate("lic_permanent");
				StatusColor = "#22C55E";
				ExpirationDisplay = _l10n.Translate("lic_permanent_label");
			}
			else if (info.ExpirationDate < DateTime.Now)
			{
				StatusMessage = string.Format(_l10n.Translate("lic_expired"),
					info.ExpirationDate.ToString("dd/MM/yyyy HH:mm"));
				StatusColor = "#EF4444";
				ExpirationDisplay = info.ExpirationDate.ToString("dd/MM/yyyy HH:mm");
			}
			else if (info.LastRunDate > DateTime.Now.AddHours(1))
			{
				StatusMessage = _l10n.Translate("lic_clock_error");
				StatusColor = "#EF4444";
			}
			else
			{
				int daysLeft = (int)(info.ExpirationDate - DateTime.Now).TotalDays;
				StatusMessage = string.Format(_l10n.Translate("lic_trial"),
					daysLeft, info.ExpirationDate.ToString("dd/MM/yyyy"));
				StatusColor = "#22C55E";
				ExpirationDisplay = info.ExpirationDate.ToString("dd/MM/yyyy HH:mm");
			}

			_licenseRepo.UpdateLastRunDate(info);
		}

		[RelayCommand]
		public void CopyHardwareId()
		{
			try
			{
				Clipboard.SetText(HardwareId);
				ActivationMessage = _l10n.Translate("lic_copied");
			}
			catch { }
		}

		[RelayCommand]
		public void ActivateLicense()
		{
			if (string.IsNullOrWhiteSpace(ActivationKey))
			{
				ActivationMessage = _l10n.Translate("lic_enter_key");
				return;
			}

			bool success = _licenseRepo.ActivateLicense(ActivationKey, out string message);
			ActivationMessage = message;

			if (success)
			{
				ActivationKey = "";
				RefreshStatus();
			}
		}
	}
}
