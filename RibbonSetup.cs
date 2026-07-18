using System;
using System.ComponentModel;
using System.Windows.Input;
using Prima.VinaCAD.ApplicationServices;
using Teigha.Runtime;
using Teigha.DatabaseServices;
using Teigha.Windows;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace TPL
{
	public class RibbonSetup : IExtensionApplication
	{
		private const string TabId = "TH_TOOLS_TAB";
		private const string TabTitle = "TH Tools";
		private readonly RibbonCommandHandler _cmdHandler = new();
		private readonly System.Collections.Generic.HashSet<Database> _hookedDatabases = new();
		private static System.Runtime.Loader.AssemblyLoadContext _pluginLoadContext;
		private bool _componentManagerSubscribed;

		public void Initialize()
		{
			RegisterDependencyResolver();
			SubscribeRibbonLifecycle();

			Application.DocumentManager.DocumentActivated += DocumentManager_DocumentActivated;
			Application.DocumentManager.DocumentCreated += DocumentManager_DocumentCreated;
			Application.DocumentManager.DocumentToBeDestroyed += DocumentManager_DocumentToBeDestroyed;

			var doc = Application.DocumentManager.MdiActiveDocument;
			if (doc != null)
			{
				HookDatabase(doc.Database);
			}
			TryCreateRibbon();
		}

		public void Terminate()
		{
			Application.DocumentManager.DocumentActivated -= DocumentManager_DocumentActivated;
			Application.DocumentManager.DocumentCreated -= DocumentManager_DocumentCreated;
			Application.DocumentManager.DocumentToBeDestroyed -= DocumentManager_DocumentToBeDestroyed;
			if (_componentManagerSubscribed)
			{
				ComponentManager.PropertyChanged -= ComponentManager_PropertyChanged;
				_componentManagerSubscribed = false;
			}
			UnhookAllDatabases();
			if (_pluginLoadContext != null)
			{
				_pluginLoadContext.Resolving -= ResolvePluginDependency;
				_pluginLoadContext = null;
			}
		}

		/// <summary>
		/// VinaCAD loads managed plugins in its own AssemblyLoadContext. Resolve NuGet
		/// dependencies from the bundle folder in that same context, never in Default.
		/// </summary>
		private static void RegisterDependencyResolver()
		{
			var pluginAssembly = typeof(RibbonSetup).Assembly;
			_pluginLoadContext = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(pluginAssembly);
			if (_pluginLoadContext != null)
			{
				_pluginLoadContext.Resolving -= ResolvePluginDependency;
				_pluginLoadContext.Resolving += ResolvePluginDependency;
			}
		}

		private static System.Reflection.Assembly ResolvePluginDependency(
			System.Runtime.Loader.AssemblyLoadContext context,
			System.Reflection.AssemblyName requestedAssembly)
		{
			try
			{
				string pluginFolder = System.IO.Path.GetDirectoryName(typeof(RibbonSetup).Assembly.Location);
				string dependencyPath = System.IO.Path.Combine(pluginFolder, requestedAssembly.Name + ".dll");
				if (!System.IO.File.Exists(dependencyPath)) return null;

				var localAssembly = System.Reflection.AssemblyName.GetAssemblyName(dependencyPath);
				if (!string.Equals(localAssembly.Name, requestedAssembly.Name, StringComparison.OrdinalIgnoreCase))
					return null;

				return context.LoadFromAssemblyPath(dependencyPath);
			}
			catch (System.Exception ex)
			{
				try
				{
					string pluginFolder = System.IO.Path.GetDirectoryName(typeof(RibbonSetup).Assembly.Location);
					System.IO.File.AppendAllText(
						System.IO.Path.Combine(pluginFolder, "tpl_dependency_log.txt"),
						$"[{DateTime.Now:O}] {requestedAssembly.FullName}: {ex}\r\n");
				}
				catch { }
				return null;
			}
		}

		private void HookDatabase(Database db)
		{
			if (db == null) return;
			lock (_hookedDatabases)
			{
				if (_hookedDatabases.Add(db))
				{
					db.SystemVariableChanged += Database_SystemVariableChanged;
				}
			}
		}

		private void UnhookDatabase(Database db)
		{
			if (db == null) return;
			lock (_hookedDatabases)
			{
				if (_hookedDatabases.Remove(db))
				{
					try { db.SystemVariableChanged -= Database_SystemVariableChanged; } catch { }
				}
			}
		}

		private void UnhookAllDatabases()
		{
			lock (_hookedDatabases)
			{
				foreach (var db in _hookedDatabases)
				{
					try { db.SystemVariableChanged -= Database_SystemVariableChanged; } catch { }
				}
				_hookedDatabases.Clear();
			}
		}

		private void DocumentManager_DocumentActivated(object sender, DocumentCollectionEventArgs e)
		{
			if (e.Document != null)
			{
				HookDatabase(e.Document.Database);
				TryCreateRibbon();
			}
		}

		private void DocumentManager_DocumentCreated(object sender, DocumentCollectionEventArgs e)
		{
			if (e.Document != null)
			{
				HookDatabase(e.Document.Database);
			}
		}

		private void DocumentManager_DocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
		{
			if (e.Document != null)
			{
				UnhookDatabase(e.Document.Database);
			}
		}

		private void Database_SystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
		{
			if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase) && ComponentManager.Ribbon != null)
			{
				CreateRibbon();
			}
		}

		private void SubscribeRibbonLifecycle()
		{
			if (_componentManagerSubscribed) return;
			ComponentManager.PropertyChanged += ComponentManager_PropertyChanged;
			_componentManagerSubscribed = true;
		}

		private void ComponentManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (string.IsNullOrEmpty(e?.PropertyName)
				|| string.Equals(e.PropertyName, "Ribbon", StringComparison.OrdinalIgnoreCase))
			{
				TryCreateRibbon();
			}
		}

		private void TryCreateRibbon()
		{
			if (ComponentManager.Ribbon != null)
				CreateRibbon();
		}

		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
		private static extern bool DeleteObject(IntPtr hObject);

		/// <summary>Load ảnh PNG từ assembly và resize để tránh lỗi scale/crop trên Ribbon VinaCAD.</summary>
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
					RibbonPanelSource rps = new() { Title = "TPL Plotter", Id = panelId };
					RibbonPanel rp = new() { Source = rps };

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
					RibbonButton btnLicense = new()
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

				// Loại bỏ prefix Ctrl+C nếu có, chỉ lấy tên lệnh thực.
				// Gửi cancel + command trong cùng buffer để prompt chọn Window
				// (ví dụ "Specify opposite corner") không nuốt mất lệnh TPL.
				string cleanCmd = cmd.Replace("\x03", "").Trim();
				if (string.IsNullOrEmpty(cleanCmd)) return;

				doc.SendStringToExecute("\x1B\x1B" + cleanCmd + "\n", true, false, false);
			}
		}
	}
}
