using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using System;

namespace TPL
{
    public class RibbonSetup : IExtensionApplication
    {
        private static bool _isLoaded = false;

        public void Initialize()
        {
            ComponentManager.ItemInitialized += ComponentManager_ItemInitialized;
            if (ComponentManager.Ribbon != null)
            {
                CreateRibbon();
            }
        }

        public void Terminate()
        {
        }

        private void ComponentManager_ItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon != null && !_isLoaded)
            {
                CreateRibbon();
                ComponentManager.ItemInitialized -= ComponentManager_ItemInitialized;
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

                // 1. Tab "TH Tools"
                RibbonTab tab = ribbon.FindTab("TH_TOOLS_TAB");
                if (tab == null)
                {
                    tab = new RibbonTab
                    {
                        Title = "TH Tools",
                        Id = "TH_TOOLS_TAB"
                    };
                    ribbon.Tabs.Add(tab);
                }

                // Kiểm tra xem Panel đã có chưa để tránh bị duplicate khi netload lại
                bool hasPanel = false;
                foreach (RibbonPanel p in tab.Panels)
                {
                    if (p.Source.Title == "TPL Plotter")
                    {
                        hasPanel = true;
                        break;
                    }
                }

                if (!hasPanel)
                {
                    // 2. Panel "TPL Plotter"
                    RibbonPanelSource panelSource = new()
                    {
                        Title = "TPL Plotter"
                    };
                    RibbonPanel panel = new()
                    {
                        Source = panelSource
                    };
                    tab.Panels.Add(panel);

                    var cmdHandler = new RibbonCommandHandler();

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
                    RibbonButton btnTpl = new()
                    {
                        Text = "\nTPL Plotter", // Thêm \n để hạ thấp text xuống 1 chút
                        ShowText = true,
                        ShowImage = true,
                        CommandParameter = "\x1B\x1BTPL ", // ESC ESC TPL
                        CommandHandler = cmdHandler,
                        Size = RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical
                    };
                    if (tplIconLarge != null) btnTpl.LargeImage = tplIconLarge;
                    if (tplIconSmall != null) btnTpl.Image = tplIconSmall;

                    // 4. Button "TPL License"
                    RibbonButton btnLicense = new()
                    {
                        Text = "\nTPL License", // Thêm \n để hạ thấp text xuống 1 chút
                        ShowText = true,
                        ShowImage = true,
                        CommandParameter = "\x1B\x1BTPL_LICENSE ",
                        CommandHandler = cmdHandler,
                        Size = RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical
                    };
                    if (licenseIconLarge != null) btnLicense.LargeImage = licenseIconLarge;
                    if (licenseIconSmall != null) btnLicense.Image = licenseIconSmall;

                    // Thêm vào Panel — bố cục hàng ngang (không dùng RibbonRowBreak)
                    panelSource.Items.Add(btnTpl);
                    panelSource.Items.Add(btnLicense);
                }

                _isLoaded = true;
                tab.IsActive = true;
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[TPL] Error loading ribbon: {ex.Message}\n");
            }
        }
    }

    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public bool CanExecute(object parameter) => true;
#pragma warning disable CS0067 // Event never used
        public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067

        public void Execute(object parameter)
        {
            if (parameter is RibbonButton button && button.CommandParameter != null)
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                doc?.SendStringToExecute((string)button.CommandParameter, true, false, false);
            }
        }
    }
}
