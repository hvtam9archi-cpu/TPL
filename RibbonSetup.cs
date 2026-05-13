using System;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
    public class RibbonSetup : IExtensionApplication
    {
        private const string TabId = "TH_TOOLS_TAB";
        private const string TabTitle = "TH Tools";
        private RibbonCommandHandler _cmdHandler = new RibbonCommandHandler();

        public void Initialize()
        {
            Application.Idle += Application_Idle;
            Application.SystemVariableChanged += Application_SystemVariableChanged;
        }

        public void Terminate() 
        {
            Application.Idle -= Application_Idle;
            Application.SystemVariableChanged -= Application_SystemVariableChanged;
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                Application.Idle -= Application_Idle;
                CreateRibbon();
            }
        }

        private void Application_SystemVariableChanged(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase) && ComponentManager.Ribbon != null)
            {
                CreateRibbon();
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>Load ảnh PNG trực tiếp từ assembly manifest stream và force resize bằng System.Drawing để chống lỗi scale/crop của AutoCAD.</summary>
        private static System.Windows.Media.ImageSource LoadEmbeddedImage(string resourceName, int size)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                using var drawingImg = System.Drawing.Image.FromStream(stream);
                // Ép khung cứng về kích thước đích (32x32 hoặc 16x16)
                using var bmp = new System.Drawing.Bitmap(drawingImg, new System.Drawing.Size(size, size));
                IntPtr hBitmap = bmp.GetHbitmap();
                try
                {
                    var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }

        private void CreateRibbon()
        {
            try
            {
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;

                // 1. Tìm hoặc Tạo Tab "TH Tools"
                RibbonTab rtb = ribbon.FindTab(TabId);
                if (rtb == null)
                {
                    rtb = new RibbonTab { Title = TabTitle, Id = TabId };
                    ribbon.Tabs.Add(rtb);
                }

                // 2. Tìm hoặc Tạo Panel "TPL Plotter"
                string panelId = "TPL_PLOTTER_PANEL";
                bool panelExists = false;
                foreach (RibbonPanel p in rtb.Panels)
                {
                    if (p.Source.Id == panelId || p.Source.Title == "TPL Plotter")
                    {
                        panelExists = true;
                        break;
                    }
                }

                if (!panelExists)
                {
                    RibbonPanelSource rps = new RibbonPanelSource { Title = "TPL Plotter", Id = panelId };
                    RibbonPanel rp = new RibbonPanel { Source = rps };

                    // Load icons từ embedded resource với kích thước chuẩn xác
                    System.Windows.Media.ImageSource tplIconLarge = null;
                    System.Windows.Media.ImageSource tplIconSmall = null;
                    System.Windows.Media.ImageSource licenseIconLarge = null;
                    System.Windows.Media.ImageSource licenseIconSmall = null;

                    try
                    {
                        tplIconLarge = LoadEmbeddedImage("TPL.Resource.IconRibbon_32px.png", 32);
                        tplIconSmall = LoadEmbeddedImage("TPL.Resource.IconRibbon_32px.png", 16);
                        licenseIconLarge = LoadEmbeddedImage("TPL.Resource.IconRibbon_License_32px.png", 32);
                        licenseIconSmall = LoadEmbeddedImage("TPL.Resource.IconRibbon_License_32px.png", 16);
                    }
                    catch { }

                    // 3. Button "TPL Plotter"
                    RibbonButton btnTpl = new RibbonButton
                    {
                        Id = "TPL_PLOTTER",
                        Text = "\nTPL Plotter", // Thêm \n để hạ thấp text xuống 1 chút
                        ShowText = true,
                        ShowImage = true,
                        Size = RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical,
                        CommandParameter = "\x03\x03TPL ",
                        CommandHandler = _cmdHandler
                    };
                    if (tplIconLarge != null) btnTpl.LargeImage = tplIconLarge;
                    if (tplIconSmall != null) btnTpl.Image = tplIconSmall;

                    // 4. Button "TPL License"
                    RibbonButton btnLicense = new RibbonButton
                    {
                        Id = "TPL_LICENSE",
                        Text = "\nTPL License", // Thêm \n để hạ thấp text xuống 1 chút
                        ShowText = true,
                        ShowImage = true,
                        Size = RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical,
                        CommandParameter = "\x03\x03TPL_LICENSE ",
                        CommandHandler = _cmdHandler
                    };
                    if (licenseIconLarge != null) btnLicense.LargeImage = licenseIconLarge;
                    if (licenseIconSmall != null) btnLicense.Image = licenseIconSmall;

                    // Thêm vào Panel — bố cục hàng ngang (không dùng RibbonRowBreak)
                    rps.Items.Add(btnTpl);
                    rps.Items.Add(btnLicense);

                    rtb.Panels.Add(rp);
                }

                rtb.IsActive = true;
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[TPL] Error loading ribbon: {ex.Message}\n");
            }
        }
    }

    public class RibbonCommandHandler : ICommand
    {
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            string cmd = null;
            if (parameter is RibbonButton btn)
                cmd = btn.CommandParameter as string;
            else if (parameter is string s)
                cmd = s;

            if (!string.IsNullOrEmpty(cmd))
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.SendStringToExecute(cmd, true, false, true);
                }
            }
        }
    }
}
