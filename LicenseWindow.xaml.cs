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

			if (_info.IsHardwareChanged)
			{
				lblStatus.Text = "⚠️ Phát hiện thay đổi linh kiện phần cứng!\nBản quyền đã bị vô hiệu hoá.";
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else if (_info.ExpirationDate == DateTime.MaxValue)
			{
				lblStatus.Text = "✅ Đã kích hoạt vĩnh viễn.";
				lblStatus.Foreground = new SolidColorBrush(Colors.LimeGreen);
			}
			else if (DateTime.Now > _info.ExpirationDate)
			{
				lblStatus.Text = $"❌ Đã hết hạn sử dụng vào ngày: {_info.ExpirationDate:dd/MM/yyyy}.\nVui lòng liên hệ tác giả để nhận mã kích hoạt.";
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else if (DateTime.Now < _info.LastRunDate)
			{
				lblStatus.Text = "❌ Thời gian hệ thống bị sai lệch!\nVui lòng đồng bộ lại đồng hồ Windows.";
				lblStatus.Foreground = new SolidColorBrush(Colors.Red);
			}
			else
			{
				int daysLeft = (int)(_info.ExpirationDate - DateTime.Now).TotalDays;
				lblStatus.Text = $"✅ Đang dùng thử. Còn lại: {daysLeft} ngày.\n(Hết hạn: {_info.ExpirationDate:dd/MM/yyyy})";
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
				MessageBox.Show("Đã copy mã phần cứng vào bộ nhớ tạm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}

		private void BtnActivate_Click(object sender, RoutedEventArgs e)
		{
			string key = txtKey.Text.Trim();
			if (string.IsNullOrEmpty(key))
			{
				MessageBox.Show("Vui lòng nhập mã kích hoạt!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (LicenseManager.ActivateLicense(key, out string message))
			{
				MessageBox.Show(message, "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
				// Reload info
				_info = LicenseManager.GetLicenseInfo();
				LoadLicenseData();
				this.DialogResult = true; // Signal success if show dialog was used
			}
			else
			{
				MessageBox.Show(message, "Lỗi Kích Hoạt", MessageBoxButton.OK, MessageBoxImage.Error);
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
