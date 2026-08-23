using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	public class Commands
	{
		public static PlotHelper.PlotSettingsData LastSettings = null;
		private static MainWindow _mainWindow = null;

		[CommandMethod("TPL")]
		public void AutoPlotCommand()
		{
			try
			{
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null) return;

				// Refresh danh sách thu hồi từ xa trong background. Một lệnh thu hồi đã
				// biết được chặn ngay lập tức bởi flag IsPermanentlyRevoked (persist trong
				// registry), nên command không bao giờ block chờ network round-trip.
				LicenseManager.CheckRemoteRevokeAsync();

				// Kiểm tra bản quyền
				var license = LicenseManager.GetLicenseInfo();
				if (!license.IsValid)
				{
					var licenseWin = new LicenseWindow(license);
					if (Application.ShowModalWindow(licenseWin) != true)
					{
						doc.Editor.WriteMessage("\n[TPL] " + L10n.T("lic_invalid_or_expired") + "\n");
						return;
					}
					license = LicenseManager.GetLicenseInfo();
					if (!license.IsValid) return;
				}

				LicenseManager.UpdateLastRunDate(license);

				if (_mainWindow == null || !_mainWindow.IsLoaded)
				{
					_mainWindow = new MainWindow();
					SetOwnerToAutoCAD(_mainWindow);
					_mainWindow.Show();
				}
				else
				{
					_mainWindow.Activate();
				}
			}
			catch (System.Exception ex)
			{
				WriteErrorToEditor("TPL command failed", ex);
			}
		}

		[CommandMethod("TPL_LICENSE")]
		public void LicenseCommand()
		{
			try
			{
				var license = LicenseManager.GetLicenseInfo();
				var licenseWin = new LicenseWindow(license);
				Application.ShowModalWindow(licenseWin);
			}
			catch (System.Exception ex)
			{
				WriteErrorToEditor("TPL_LICENSE command failed", ex);
			}
		}

		/// <summary>Gán owner cho WPF window để window luôn nằm trên AutoCAD (best-effort).</summary>
		private static void SetOwnerToAutoCAD(System.Windows.Window window)
		{
			try
			{
				var acWin = Application.MainWindow;
				if (acWin == null) return;
				new System.Windows.Interop.WindowInteropHelper(window) { Owner = acWin.Handle };
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[TPL] Could not set window owner: {ex.Message}");
			}
		}

		/// <summary>Ghi lỗi: full exception vào Debug output, chỉ message ngắn gọn ra command line.</summary>
		private static void WriteErrorToEditor(string context, System.Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[TPL] {context}: {ex}");
			try
			{
				var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
				ed?.WriteMessage($"\n[TPL] Error: {ex.Message}\n");
			}
			catch (System.Exception inner)
			{
				System.Diagnostics.Debug.WriteLine($"[TPL] Could not write error to editor: {inner.Message}");
			}
		}
	}
}