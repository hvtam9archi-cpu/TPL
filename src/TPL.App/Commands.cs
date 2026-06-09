using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using TPL.Domain.Interfaces;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	/// <summary>
	/// Entry point commands — thin wrappers chỉ chứa [CommandMethod].
	/// Tất cả logic delegate qua DI → ViewModel.
	/// Sử dụng CommandGuard cho crash-proof.
	/// </summary>
	public class Commands
	{
		private static System.Windows.Window _mainWindow;

		[CommandMethod("TPL")]
		public void AutoPlotCommand()
		{
			CommandGuard.Execute("TPL", () =>
			{
				if (!ServiceContainer.IsInitialized) ServiceContainer.Initialize();

				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null) return;

				// License check
				var licenseRepo = ServiceContainer.Resolve<ILicenseRepository>();
				var license = licenseRepo.GetLicenseInfo();
				if (!license.IsValid)
				{
					// TODO: Show LicenseWindow via Presentation layer
					doc.Editor.WriteMessage("\n[TPL] License is invalid or expired.\n");
					return;
				}

				licenseRepo.UpdateLastRunDate(license);
				licenseRepo.CheckRemoteRevokeAsync();

				if (_mainWindow == null || !_mainWindow.IsLoaded)
				{
					// TODO: Create MainWindow via Presentation layer with ViewModel from DI
					// var vm = ServiceContainer.Resolve<MainWindowViewModel>();
					// _mainWindow = new MainWindow(vm);
					_mainWindow = new System.Windows.Window(); // Placeholder

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
					catch { }
					_mainWindow.Show();
				}
				else
				{
					_mainWindow.Activate();
				}
			});
		}

		[CommandMethod("TPL_LICENSE")]
		public void LicenseCommand()
		{
			CommandGuard.Execute("TPL_LICENSE", () =>
			{
				if (!ServiceContainer.IsInitialized) ServiceContainer.Initialize();

				// TODO: Create LicenseWindow via Presentation layer with ViewModel from DI
				// var vm = ServiceContainer.Resolve<LicenseWindowViewModel>();
				// var licenseWin = new LicenseWindow(vm);
				// Application.ShowModalWindow(licenseWin);
			});
		}
	}
}
