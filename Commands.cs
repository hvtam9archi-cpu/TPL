using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

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
					if (Application.ShowModalWindow(licenseWin) != true)
					{
						doc.Editor.WriteMessage("\n[TPL] Bản quyền không hợp lệ hoặc đã hết hạn.\n");
						return;
					}
					license = LicenseManager.GetLicenseInfo();
					if (!license.IsValid) return;
				}

				LicenseManager.UpdateLastRunDate(license);

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
