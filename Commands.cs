using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NDPL
{
    public class Commands
    {
        public static PlotHelper.PlotSettingsData LastSettings = null;
        private static MainForm _mainForm = null;

        [CommandMethod("NDPL")]
        public void AutoPlotCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (_mainForm == null || _mainForm.IsDisposed)
            {
                _mainForm = new MainForm();
                Application.ShowModelessDialog(Application.MainWindow.Handle, _mainForm);
            }
            else
            {
                _mainForm.Focus();
            }
        }
    }
}
