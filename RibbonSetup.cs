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
            // The ribbon might not be ready when the plugin loads on startup
            ComponentManager.ItemInitialized += ComponentManager_ItemInitialized;
            
            // If ribbon is already loaded (e.g. netload)
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
                    tab = new RibbonTab();
                    tab.Title = "TH Tools";
                    tab.Id = "TH_TOOLS_TAB";
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
                    RibbonPanelSource panelSource = new RibbonPanelSource();
                    panelSource.Title = "TPL Plotter";
                    RibbonPanel panel = new RibbonPanel();
                    panel.Source = panelSource;
                    tab.Panels.Add(panel);

                    // 3. Command Handler
                    var cmdHandler = new RibbonCommandHandler();

                    // 4. Button "TPL"
                    RibbonButton btnTpl = new RibbonButton();
                    btnTpl.Text = "TPL";
                    btnTpl.ShowText = true;
                    btnTpl.ShowImage = true;
                    btnTpl.CommandParameter = "\x1B\x1BTPL "; // ESC ESC TPL
                    btnTpl.CommandHandler = cmdHandler;
                    btnTpl.Size = RibbonItemSize.Large;
                    btnTpl.Orientation = System.Windows.Controls.Orientation.Vertical;
                    
                    // You can add Icon here if you want:
                    // btnTpl.LargeImage = LoadImage(...);

                    // 5. Button "TPL License"
                    RibbonButton btnLicense = new RibbonButton();
                    btnLicense.Text = "TPL License";
                    btnLicense.ShowText = true;
                    btnLicense.ShowImage = true;
                    btnLicense.CommandParameter = "\x1B\x1BTPL_LICENSE ";
                    btnLicense.CommandHandler = cmdHandler;
                    btnLicense.Size = RibbonItemSize.Standard;

                    // Thêm vào Panel
                    panelSource.Items.Add(btnTpl);
                    panelSource.Items.Add(new RibbonRowBreak());
                    panelSource.Items.Add(btnLicense);
                }
                
                _isLoaded = true;
                tab.IsActive = true; // (Tùy chọn) Nhảy tới tab này khi load
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
                if (doc != null)
                {
                    doc.SendStringToExecute((string)button.CommandParameter, true, false, false);
                }
            }
        }
    }
}
