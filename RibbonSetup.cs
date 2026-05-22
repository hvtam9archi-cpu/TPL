using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using System;

namespace TPL
{
    public class RibbonSetup : IExtensionApplication
    {
        private const string TabId = "TH_TOOLS_TAB";
        private const string TabTitle = "TH Tools";
        private readonly RibbonCommandHandler _cmdHandler = new RibbonCommandHandler();

        public void Initialize()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += Application_Idle;
            Autodesk.AutoCAD.ApplicationServices.Application.SystemVariableChanged += Application_SystemVariableChanged;
        }

        public void Terminate()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= Application_Idle;
            Autodesk.AutoCAD.ApplicationServices.Application.SystemVariableChanged -= Application_SystemVariableChanged;
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle -= Application_Idle;
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
                RibbonTab tab = ribbon.FindTab(TabId);
                if (tab == null)
                {
                    tab = new RibbonTab
                    {
                        Title = TabTitle,
                        Id = TabId
                    };
                    ribbon.Tabs.Add(tab);
                    tab.IsActive = true; // Ép tab hiển thị ngay lúc khởi động
                }

                // 2. Tìm hoặc Tạo Panel "TPL Plotter"
                string panelId = "TPL_PLOTTER_PANEL";
                RibbonPanel panel = null;
                foreach (RibbonPanel p in tab.Panels)
                {
                    if (p.Source.Id == panelId || p.Source.Title == "TPL Plotter")
                    {
                        panel = p;
                        break;
                    }
                }

                if (panel == null)
                {
                    RibbonPanelSource panelSource = new RibbonPanelSource
                    {
                        Title = "TPL Plotter",
                        Id = panelId
                    };
                    panel = new RibbonPanel
                    {
                        Source = panelSource
                    };

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
                        CommandParameter = "TPL",
                        CommandHandler = _cmdHandler,
                        Size = RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical
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
                        CommandParameter = "TPL_LICENSE",
                        CommandHandler = _cmdHandler,
                        Size = RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical
                    };
                    if (licenseIconLarge != null) btnLicense.LargeImage = licenseIconLarge;
                    if (licenseIconSmall != null) btnLicense.Image = licenseIconSmall;

                    // Thêm vào Panel — bố cục hàng ngang (không dùng RibbonRowBreak)
                    panelSource.Items.Add(btnTpl);
                    panelSource.Items.Add(btnLicense);

                    tab.Panels.Add(panel);
                }

                tab.IsActive = true;
            }
            catch (System.Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[TPL] Error loading ribbon: {ex.Message}\n");
            }
        }
    }

    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
#pragma warning disable CS0067 // Event never used
        public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            string cmd = null;
            if (parameter is RibbonButton button)
            {
                cmd = button.CommandParameter as string;
            }
            else if (parameter is string s)
            {
                cmd = s;
            }

            if (!string.IsNullOrEmpty(cmd))
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    // Tách thành 2 lời gọi SendStringToExecute riêng biệt để tránh dính lệnh:
                    // Gọi 1: Hủy lệnh đang chạy
                    doc.SendStringToExecute("\x1B\x1B", true, false, false);
                    // Gọi 2: Gửi tên lệnh (buffer riêng, không bị dính)
                    doc.SendStringToExecute(cmd + "\n", true, false, false);
                }
            }
        }
    }
}
