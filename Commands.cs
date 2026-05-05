using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
    public class Commands
    {
        public static PlotHelper.PlotSettingsData LastSettings = null;
        private static MainForm _mainForm = null;
        public static MainForm MainFormInstance => _mainForm;

        [CommandMethod("TPL")]
        public void AutoPlotCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Kiểm tra bản quyền trước khi mở
            var license = LicenseManager.GetLicenseInfo();
            if (!license.IsValid)
            {
                using (var licenseForm = new LicenseForm(license))
                {
                    if (Application.ShowModalDialog(licenseForm) != System.Windows.Forms.DialogResult.OK)
                    {
                        doc.Editor.WriteMessage("\n[TPL] Bản quyền không hợp lệ hoặc đã hết hạn.\n");
                        return; // Thoát nếu chưa kích hoạt
                    }
                }
                
                // Load lại xem đã kích hoạt thành công chưa
                license = LicenseManager.GetLicenseInfo();
                if (!license.IsValid) return;
            }

            // Cập nhật ngày dùng cuối để chống tua ngược thời gian
            LicenseManager.UpdateLastRunDate(license);

            // Gửi một luồng chạy ngầm để kiểm tra xem máy này có bị khoá từ xa hay không
            // (Không làm chậm thao tác của người dùng do chạy dưới nền)
            LicenseManager.CheckRemoteRevokeAsync();

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

        [CommandMethod("TPL_LICENSE")]
        public void LicenseCommand()
        {
            var license = LicenseManager.GetLicenseInfo();
            using (var licenseForm = new LicenseForm(license))
            {
                Application.ShowModalDialog(licenseForm);
            }
        }

    }
}
