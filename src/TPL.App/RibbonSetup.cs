using System;
using System.Reflection;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using TPL.Core.Helpers;
using TPL.Core.Logging;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	/// <summary>
	/// IExtensionApplication — khởi tạo DI, Logger, Ribbon khi AutoCAD load plugin.
	/// Sử dụng Application.Idle event để chờ Ribbon sẵn sàng (tránh NullReference).
	/// </summary>
	public class RibbonSetup : IExtensionApplication
	{
		private const string TabId = "TH_TOOLS_TAB";
		private const string TabTitle = "TH Tools";
		private readonly RibbonCommandHandler _cmdHandler = new();

		public void Initialize()
		{
			// 1. Khởi tạo Logger đầu tiên
			TplLogger.Initialize();
			TplLogger.Info("TPL Plugin loading...");

			// 2. Khởi tạo DI Container
			try
			{
				ServiceContainer.Initialize();
				TplLogger.Info("ServiceContainer initialized.");
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, "ServiceContainer.Initialize");
			}

			// 3. Đăng ký sự kiện Ribbon
			Application.Idle += Application_Idle;
			Application.SystemVariableChanged += Application_SystemVariableChanged;
		}

		public void Terminate()
		{
			Application.Idle -= Application_Idle;
			Application.SystemVariableChanged -= Application_SystemVariableChanged;
			TplLogger.Info("TPL Plugin unloading.");
			TplLogger.Shutdown();
		}

		private void Application_Idle(object sender, EventArgs e)
		{
			if (ComponentManager.Ribbon != null)
			{
				Application.Idle -= Application_Idle;
				CreateRibbon();
			}
		}

		private void Application_SystemVariableChanged(object sender,
			Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
		{
			if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase)
				&& ComponentManager.Ribbon != null)
			{
				CreateRibbon();
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
					RibbonPanelSource rps = new() { Title = "TPL Plotter", Id = panelId };
					RibbonPanel rp = new() { Source = rps };

					// Load icons từ embedded resource — sử dụng ImageHelper từ Core
					Assembly asm = Assembly.GetExecutingAssembly();
					var tplIconLarge = ImageHelper.LoadEmbeddedImage(asm, "TPL.Resource.IconRibbon_32px.png", 32);
					var tplIconSmall = ImageHelper.LoadEmbeddedImage(asm, "TPL.Resource.IconRibbon_32px.png", 16);
					var licenseIconLarge = ImageHelper.LoadEmbeddedImage(asm, "TPL.Resource.IconRibbon_License_32px.png", 32);
					var licenseIconSmall = ImageHelper.LoadEmbeddedImage(asm, "TPL.Resource.IconRibbon_License_32px.png", 16);

					// 3. Button "TPL Plotter"
					RibbonButton btnTpl = new()
					{
						Id = "TPL_PLOTTER",
						Text = "\nTPL Plotter",
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
					RibbonButton btnLicense = new()
					{
						Id = "TPL_LICENSE",
						Text = "\nTPL License",
						ShowText = true,
						ShowImage = true,
						Size = RibbonItemSize.Large,
						Orientation = System.Windows.Controls.Orientation.Vertical,
						CommandParameter = "\x03\x03TPL_LICENSE ",
						CommandHandler = _cmdHandler
					};
					if (licenseIconLarge != null) btnLicense.LargeImage = licenseIconLarge;
					if (licenseIconSmall != null) btnLicense.Image = licenseIconSmall;

					rps.Items.Add(btnTpl);
					rps.Items.Add(btnLicense);
					rtb.Panels.Add(rp);
				}

				rtb.IsActive = true;
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, "CreateRibbon");
				Application.DocumentManager.MdiActiveDocument?.Editor
					.WriteMessage($"\n[TPL] Error loading ribbon: {ex.Message}\n");
			}
		}
	}

	/// <summary>
	/// Xử lý lệnh từ Ribbon buttons.
	/// </summary>
	public class RibbonCommandHandler : ICommand
	{
		public event EventHandler CanExecuteChanged
		{
			add { }
			remove { }
		}
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
				if (doc == null) return;

				string cleanCmd = cmd.Replace("\x03", "").Trim();
				if (string.IsNullOrEmpty(cleanCmd)) return;

				doc.SendStringToExecute("\x1B\x1B", true, false, false);
				doc.SendStringToExecute(cleanCmd + "\n", true, false, false);
			}
		}
	}
}
