using System;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(TPL.RibbonSetup))]

namespace TPL
{
	public class RibbonSetup : IExtensionApplication
	{
		private const string TabId = "TH_TOOLS_TAB";
		private const string TabTitle = "TH Tools";
		private readonly RibbonCommandHandler _cmdHandler = new();

		public void Initialize()
		{
			MainWindow.SubscribeGlobalMarkerEvents();
			Application.Idle += Application_Idle;
			Application.SystemVariableChanged += Application_SystemVariableChanged;
		}

		public void Terminate()
		{
			Application.Idle -= Application_Idle;
			Application.SystemVariableChanged -= Application_SystemVariableChanged;
			MainWindow.UnsubscribeGlobalMarkerEvents();
		}

		private void Application_Idle(object sender, EventArgs e)
		{
			try
			{
				if (ComponentManager.Ribbon != null && CreateRibbon())
					Application.Idle -= Application_Idle;
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] Ribbon Idle setup: {ex}");
			}
		}

		private void Application_SystemVariableChanged(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
		{
			try
			{
				if (!string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) return;
				Application.Idle -= Application_Idle;
				Application.Idle += Application_Idle;
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] Ribbon workspace change: {ex}");
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
			catch (System.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] Cannot load ribbon image '{resourceName}': {ex}");
				return null;
			}
		}

		private bool CreateRibbon()
		{
			try
			{
				RibbonControl ribbon = ComponentManager.Ribbon;
				if (ribbon == null) return false;

				// 1. Tìm hoặc Tạo Tab "TH Tools"
				RibbonTab rtb = ribbon.FindTab(TabId);
				if (rtb == null)
				{
					rtb = new RibbonTab { Title = TabTitle, Id = TabId };
					ribbon.Tabs.Add(rtb);
					rtb.IsActive = true;
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

					// Load icons từ embedded resource với kích thước chuẩn xác
					var tplIconLarge = LoadEmbeddedImage("TPL.Resource.IconRibbon_32px.png", 32);
					var tplIconSmall = LoadEmbeddedImage("TPL.Resource.IconRibbon_32px.png", 16);

					// 3. Button "TPL Plotter"
					RibbonButton btnTpl = new()
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

					// Thêm vào Panel — bố cục hàng ngang (không dùng RibbonRowBreak)
					rps.Items.Add(btnTpl);

					rtb.Panels.Add(rp);
				}

				rtb.IsActive = true;
				return true;
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] Create ribbon: {ex}");
				return false;
			}
		}
	}

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

				// Tách: lệnh cancel (\x1B\x1B) đi trước, tên command đi sau (buffer riêng)
				// Loại bỏ prefix \x03 nếu có, chỉ lấy tên lệnh thực
				string cleanCmd = cmd.Replace("\x03", "").Trim();
				if (string.IsNullOrEmpty(cleanCmd)) return;

				doc.SendStringToExecute("\x1B\x1B", true, false, false);
				doc.SendStringToExecute(cleanCmd + "\n", true, false, false);
			}
		}
	}
}
