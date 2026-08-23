using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;

namespace TPL
{
	public class PlotHelper
	{
		public enum FrameType
		{
			Block,
			Polyline
		}

		public enum SelectionMode
		{
			CurrentLayout = 1,
			Manual = 2
		}

		public enum SortOrder
		{
			LeftToRight,
			RightToLeft,
			TopToBottom,
			BottomToTop,
			SelectionOrder,
			None = 6
		}

		public enum BasePoint
		{
			BottomLeft,
			BottomRight,
			TopLeft,
			TopRight
		}

		public enum PlotOrientation
		{
			Auto,
			Portrait,
			Landscape
		}

		public class PlotSettingsData
		{
			public string DeviceName { get; set; }
			public string PaperSize { get; set; }
			public string PlotStyle { get; set; }
			public string OutputPath { get; set; }
			public string BaseFileName { get; set; }
			public FrameType FrameType { get; set; }
			public List<string> FrameNames { get; set; } = new List<string>();
			public SelectionMode SelectionMode { get; set; }
			public SortOrder GroupOrder { get; set; } // ORD1
			public SortOrder CrossGroupOrder { get; set; } // ORD2
			public BasePoint SortBasePoint { get; set; }
			public PlotOrientation Orientation { get; set; }
			public double Fuzz { get; set; }
			public bool MarkPlotRegions { get; set; }
			public bool MergePdfs { get; set; }
			public bool OpenPdf { get; set; }
			public bool ConvertToImage { get; set; }
			public bool PdfEditor { get; set; }
			public string ImageFormat { get; set; } = "PNG";
			public int ImageDpi { get; set; } = 600;
			public List<ObjectId> ManualSelectionIds { get; set; } = new List<ObjectId>();
		}

		public static List<string> GetPrinters()
		{
			PlotSettingsValidator psv = PlotSettingsValidator.Current;
			return psv.GetPlotDeviceList().Cast<string>().ToList();
		}

		public static List<string> GetPaperSizes(string deviceName)
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			List<string> papers = new();
			if (doc == null || doc.IsDisposed || string.IsNullOrWhiteSpace(deviceName)) return papers;

			using (doc.LockDocument())
			using (PlotSettings ps = new(doc.Database.TileMode))
			{
				try
				{
					PlotSettingsValidator psv = PlotSettingsValidator.Current;
					psv.SetPlotConfigurationName(ps, deviceName, null);
					psv.RefreshLists(ps);
					papers = psv.GetCanonicalMediaNameList(ps).Cast<string>().Select(p => p.Replace("_", " ")).ToList();
				}
				catch (System.Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[TPL] GetPaperSizes error for '{deviceName}': {ex.Message}");
				}
			}
			return papers;
		}

		/// <summary>Maps the paper name displayed in the UI back to AutoCAD's canonical media name.</summary>
		public static string ResolveCanonicalPaperSize(PlotSettingsValidator validator, PlotSettings settings, string displayName)
		{
			if (string.IsNullOrWhiteSpace(displayName)) return null;

			string normalizedName = displayName.Replace(" ", "_");
			foreach (string canonicalName in validator.GetCanonicalMediaNameList(settings).Cast<string>())
			{
				if (string.Equals(canonicalName, normalizedName, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(canonicalName.Replace("_", " "), displayName, StringComparison.OrdinalIgnoreCase))
					return canonicalName;
			}

			return null;
		}

		public static List<string> GetPlotStyles()
		{
			PlotSettingsValidator psv = PlotSettingsValidator.Current;
			return psv.GetPlotStyleSheetList().Cast<string>().ToList();
		}

		/// <summary>
		/// Kiểm tra xem device có phải máy in xuất file (PDF, DWF, PLT...) hay máy in vật lý.
		/// Trả về true nếu là máy in file, false nếu là máy in vật lý (Canon, HP, Epson...).
		/// </summary>
		public static bool IsFilePrinter(string deviceName)
		{
			if (string.IsNullOrWhiteSpace(deviceName)) return false;

			// SetCurrentConfig thay đổi global state của AutoCAD — lưu thiết bị hiện hành
			// và luôn khôi phục trong finally kể cả khi exception hoặc early-return.
			string previousDevice = null;
			try { previousDevice = PlotConfigManager.CurrentConfig?.DeviceName; }
			catch (System.Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[TPL] Could not read current plot config: {ex.Message}");
			}

			try
			{
				PlotConfigManager.SetCurrentConfig(deviceName);
				PlotConfig config = PlotConfigManager.CurrentConfig;
				if (config != null) return config.IsPlotToFile;
			}
			catch (System.Exception ex)
			{
				// Fall back to names only if AutoCAD cannot load the selected plot config.
				System.Diagnostics.Debug.WriteLine($"[TPL] Could not load plot config '{deviceName}': {ex.Message}");
			}
			finally
			{
				if (!string.IsNullOrEmpty(previousDevice) &&
					!string.Equals(previousDevice, deviceName, StringComparison.OrdinalIgnoreCase))
				{
					try { PlotConfigManager.SetCurrentConfig(previousDevice); }
					catch (System.Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[TPL] Could not restore plot config '{previousDevice}': {ex.Message}");
					}
				}
			}

			string lower = deviceName.ToLowerInvariant();
			// Các keyword nhận diện máy in xuất file (pc3 driver hoặc system printer ảo)
			string[] fileKeywords = { "pdf", "dwf", "dwg", "png", "jpg", "jpeg", "tiff", "svg", "eps", "plt",
				"publish to web", "dwfx", "design review" };
			return fileKeywords.Any(k => lower.Contains(k));
		}
	}
}
