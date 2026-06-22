using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TPL
{
	public partial class LicenseWindow : Window
	{
		private LicenseInfo _info;

		public LicenseWindow(LicenseInfo info)
		{
			_info = info;
			InitializeComponent();
			LoadLicenseData();
		}

		private void LoadLicenseData()
		{
			txtHwId.Text = _info.HardwareId;

			if (_info.IsPermanentlyRevoked)
			{
				lblStatus.Text = L10n.T("lic_revoked_detected");
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else if (_info.IsHardwareChanged)
			{
				lblStatus.Text = L10n.T("lic_hw_changed");
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else if (_info.ExpirationDate == DateTime.MaxValue)
			{
				lblStatus.Text = L10n.T("lic_permanent");
				lblStatus.Foreground = new SolidColorBrush(Colors.LimeGreen);
			}
			else if (DateTime.Now > _info.ExpirationDate)
			{
				lblStatus.Text = string.Format(L10n.T("lic_expired"), _info.ExpirationDate.ToString("dd/MM/yyyy"));
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else if (DateTime.Now < _info.LastRunDate)
			{
				lblStatus.Text = L10n.T("lic_clock_error");
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else
			{
				int daysLeft = (int)(_info.ExpirationDate - DateTime.Now).TotalDays;
				lblStatus.Text = string.Format(L10n.T("lic_trial"), daysLeft, _info.ExpirationDate.ToString("dd/MM/yyyy"));
				lblStatus.Foreground = new SolidColorBrush(Colors.Orange);
			}
		}

		private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			DragMove();
		}

		private void BtnCopy_Click(object sender, RoutedEventArgs e)
		{
			if (!string.IsNullOrEmpty(txtHwId.Text))
			{
				Clipboard.SetText(txtHwId.Text);
				MessageBox.Show(L10n.T("lic_copied"), L10n.T("lic_info"), MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}

		private void BtnActivate_Click(object sender, RoutedEventArgs e)
		{
			string key = txtKey.Text.Trim();
			if (string.IsNullOrEmpty(key))
			{
				MessageBox.Show(L10n.T("lic_enter_key"), L10n.T("err_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (LicenseManager.ActivateLicense(key, out string message))
			{
				MessageBox.Show(message, L10n.T("lic_success"), MessageBoxButton.OK, MessageBoxImage.Information);
				// Reload info
				_info = LicenseManager.GetLicenseInfo();
				LoadLicenseData();
				this.DialogResult = true; // Signal success if show dialog was used
			}
			else
			{
				MessageBox.Show(message, L10n.T("lic_activate_error"), MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void BtnClose_Click(object sender, RoutedEventArgs e)
		{
			if (_info.IsValid)
				this.DialogResult = true;
			else
				this.DialogResult = false;
			this.Close();
		}
	}
}
