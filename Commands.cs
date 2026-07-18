using Prima.VinaCAD.ApplicationServices;
using Teigha.Runtime;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace TPL
{
	public class Commands
	{
		public static PlotHelper.PlotSettingsData LastSettings = null;
		private static MainWindow _mainWindow = null;
		public static MainWindow MainFormInstance => _mainWindow;

		[CommandMethod("TPL")]
		public void AutoPlotCommand()
		{
			try
			{
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null) return;

				// Tạm thời vô hiệu hoá phần kiểm tra bản quyền
				/*
				// Kiểm tra revoke từ xa TRƯỚC — để revoke có hiệu lực ngay lập tức
				if (LicenseManager.CheckRemoteRevokeSync())
				{
					doc.Editor.WriteMessage("\n[TPL] Bản quyền đã bị thu hồi từ xa.\n");
				}

				// Đồng thời chạy async để cập nhật revoke list trong background
				LicenseManager.CheckRemoteRevokeAsync();

				// Kiểm tra bản quyền
				var license = LicenseManager.GetLicenseInfo();
				if (!license.IsValid)
				{
					var licenseWin = new LicenseWindow(license);
					Application.ShowModalWindow(licenseWin);
					license = LicenseManager.GetLicenseInfo();
					if (!license.IsValid)
					{
						doc.Editor.WriteMessage("\n[TPL] Bản quyền không hợp lệ hoặc đã hết hạn.\n");
						return;
					}
				}

				LicenseManager.UpdateLastRunDate(license);
				*/

				if (_mainWindow == null || !_mainWindow.IsLoaded)
				{
					_mainWindow = new MainWindow();
					// Gán owner an toàn — Application.MainWindow có thể null
					try
					{
						var acWin = Application.MainWindow;
						if (acWin != null)
						{
							var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow)
							{
								Owner = acWin.Handle
							};
						}
					}
					catch { /* Bỏ qua nếu không lấy được HWND */ }
					_mainWindow.Show();
				}
				else
				{
					// Activate() alone cannot restore a modeless WPF window that
					// was hidden while VinaCAD was collecting a selection.
					if (!_mainWindow.IsVisible)
						_mainWindow.Show();
					if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
						_mainWindow.WindowState = System.Windows.WindowState.Normal;
					_mainWindow.Activate();
				}
			}
			catch (System.Exception ex)
			{
				try
				{
					var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
					ed?.WriteMessage($"\n[TPL] Error: {ex}\n");
				}
				catch { }
			}
		}

		[CommandMethod("TPL_LICENSE")]
		public void LicenseCommand()
		{
			var license = LicenseManager.GetLicenseInfo();
			var licenseWin = new LicenseWindow(license);
			Application.ShowModalWindow(licenseWin);
		}

	}
}
