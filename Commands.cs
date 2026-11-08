using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TPL.Commands))]

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

				if (_mainWindow == null || !_mainWindow.IsLoaded)
				{
					_mainWindow = new MainWindow();
					Application.ShowModelessWindow(_mainWindow);
				}
				else
				{
					if (!_mainWindow.IsVisible) _mainWindow.Show();
					_mainWindow.Activate();
				}
			}
			catch (Autodesk.AutoCAD.Runtime.Exception ex)
			{
				ReportCommandError(ex);
			}
			catch (System.Exception ex)
			{
				ReportCommandError(ex);
			}
		}

		private static void ReportCommandError(System.Exception exception)
		{
			System.Diagnostics.Trace.WriteLine($"[TPL] TPL command: {exception}");
			try
			{
				Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[TPL] Error: {exception.Message}\n");
			}
			catch (System.Exception loggingException)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] Cannot report command error: {loggingException}");
			}
		}

	}
}
