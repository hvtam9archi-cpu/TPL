using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TPL.Domain.Enums;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;

namespace TPL.Presentation.ViewModels
{
	/// <summary>
	/// ViewModel chính cho MainWindow.
	/// Sử dụng CommunityToolkit.Mvvm [ObservableProperty] + [RelayCommand].
	/// KHÔNG chứa AutoCAD API calls — tất cả delegate qua IDrawingQueryService/IPlotService.
	/// </summary>
	public partial class MainWindowViewModel : ObservableObject
	{
		private readonly IDrawingQueryService _queryService;
		private readonly IPlotService _plotService;
		private readonly IFrameSortingService _sortingService;
		private readonly IPdfPostProcessor _pdfProcessor;
		private readonly IMarkerService _markerService;
		private readonly ILocalizationService _l10n;

		public MainWindowViewModel(
			IDrawingQueryService queryService,
			IPlotService plotService,
			IFrameSortingService sortingService,
			IPdfPostProcessor pdfProcessor,
			IMarkerService markerService,
			ILocalizationService l10n)
		{
			_queryService = queryService;
			_plotService = plotService;
			_sortingService = sortingService;
			_pdfProcessor = pdfProcessor;
			_markerService = markerService;
			_l10n = l10n;
		}

		// ─── Printer / Paper / Style ─────────────────────────────────────

		[ObservableProperty]
		private readonly ObservableCollection<string> printers = new();

		[ObservableProperty]
		private readonly string selectedPrinter;

		[ObservableProperty]
		private readonly ObservableCollection<string> paperSizes = new();

		[ObservableProperty]
		private readonly string selectedPaperSize;

		[ObservableProperty]
		private readonly ObservableCollection<string> plotStyles = new();

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly string selectedPlotStyle;

		// ─── Frame Selection ─────────────────────────────────────────────

		[ObservableProperty]
=======
		private readonly string selectedPlotStyle;

		// ─── Frame Selection ─────────────────────────────────────────────

		[ObservableProperty]
>>>>>>> After
		private readonly string selectedPlotStyle;

		// ─── Frame Selection ─────────────────────────────────────────────

		[ObservableProperty]
		private readonly bool isBlockMode = true;

		[ObservableProperty]
		private readonly string blockNames = "";

		[ObservableProperty]
		private readonly string layerNames = "";

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly List<string> frameNameList = new();

		// ─── Output ──────────────────────────────────────────────────────

		[ObservableProperty]
=======
		private readonly List<string> frameNameList = new();

		// ─── Output ──────────────────────────────────────────────────────

		[ObservableProperty]
>>>>>>> After
		private readonly List<string> frameNameList = new();

		// ─── Output ──────────────────────────────────────────────────────

		[ObservableProperty]
		private readonly string baseFileName = "";

		[ObservableProperty]
		private readonly string outputPath = "";

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly bool isFilePrinter = true;

		// ─── Options ─────────────────────────────────────────────────────

		[ObservableProperty]
=======
		private readonly bool isFilePrinter = true;

		// ─── Options ─────────────────────────────────────────────────────

		[ObservableProperty]
>>>>>>> After
		private readonly bool isFilePrinter = true;

		// ─── Options ─────────────────────────────────────────────────────

		[ObservableProperty]
		private readonly bool mergePdfs = true;

		[ObservableProperty]
		private readonly bool openWhenDone = true;

		[ObservableProperty]
		private readonly bool markPlotRegions;

		[ObservableProperty]
		private readonly bool pdfEditorEnabled;

		[ObservableProperty]
		private readonly bool convertToImage;

		[ObservableProperty]
		private readonly string imageFormat = "JPG";

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly int imageDpi = 600;

		// ─── Scope ───────────────────────────────────────────────────────

		[ObservableProperty]
=======
		private readonly int imageDpi = 600;

		// ─── Scope ───────────────────────────────────────────────────────

		[ObservableProperty]
>>>>>>> After
		private readonly int imageDpi = 600;

		// ─── Scope ───────────────────────────────────────────────────────

		[ObservableProperty]
		private readonly bool isAutoSelect = true;

		[ObservableProperty]
		private readonly bool isManualSelect;

		[ObservableProperty]
		private readonly int manualSelectionCount;

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly List<long> manualSelectionHandles = new();

		// ─── Sort ────────────────────────────────────────────────────────

		[ObservableProperty]
=======
		private readonly List<long> manualSelectionHandles = new();

		// ─── Sort ────────────────────────────────────────────────────────

		[ObservableProperty]
>>>>>>> After
		private readonly List<long> manualSelectionHandles = new();

		// ─── Sort ────────────────────────────────────────────────────────

		[ObservableProperty]
		private readonly ObservableCollection<string> sortOrderItems = new();

		[ObservableProperty]
		private readonly int selectedOrd1Index;

		[ObservableProperty]
		private readonly int selectedOrd2Index;

		[ObservableProperty]
		private readonly ObservableCollection<string> basePointItems = new();

		[ObservableProperty]
		private readonly int selectedBasePointIndex;

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly double fuzz = 100;

		// ─── Orientation ─────────────────────────────────────────────────

		[ObservableProperty]
		private readonly PlotOrientation orientation = PlotOrientation.Auto;

		// ─── Status ──────────────────────────────────────────────────────

		[ObservableProperty]
=======
		private readonly double fuzz = 100;

		// ─── Orientation ─────────────────────────────────────────────────

		[ObservableProperty]
		private readonly PlotOrientation orientation = PlotOrientation.Auto;

		// ─── Status ──────────────────────────────────────────────────────

		[ObservableProperty]
>>>>>>> After
		private readonly double fuzz = 100;

		// ─── Orientation ─────────────────────────────────────────────────

		[ObservableProperty]
		private readonly PlotOrientation orientation = PlotOrientation.Auto;

		// ─── Status ──────────────────────────────────────────────────────

		[ObservableProperty]
		private readonly string statusText = "Ready";

		[ObservableProperty]
		private readonly int frameCount;

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly bool isPlotting;

		// ─── Window Title ────────────────────────────────────────────────

		public string WindowTitle => _l10n?.Translate("app_title") ?? "Batch Plot PDF — TPL";

		// ═══════════════════════════════════════════════════════════════════
		// METHODS
		// ═══════════════════════════════════════════════════════════════════

		/// <summary>Khởi tạo dữ liệu — gọi từ Window.Loaded (Main thread).</summary>
=======
		private readonly bool isPlotting;

		// ─── Window Title ────────────────────────────────────────────────

		public string WindowTitle => _l10n?.Translate("app_title") ?? "Batch Plot PDF — TPL";

		// ═══════════════════════════════════════════════════════════════════
		// METHODS
		// ═══════════════════════════════════════════════════════════════════

		/// <summary>Khởi tạo dữ liệu — gọi từ Window.Loaded (Main thread).</summary>
>>>>>>> After
		private readonly bool isPlotting;

		// ─── Window Title ────────────────────────────────────────────────

		public string WindowTitle => _l10n?.Translate("app_title") ?? "Batch Plot PDF — TPL";

		// ═══════════════════════════════════════════════════════════════════
		// METHODS
		// ═══════════════════════════════════════════════════════════════════

		/// <summary>Khởi tạo dữ liệu — gọi từ Window.Loaded (Main thread).</summary>
		public void LoadData()
		{
			// Printers
			Printers = new ObservableCollection<string>(_queryService.GetPrinters());
			if (Printers.Count > 0) SelectedPrinter = Printers[0];

			// Plot Styles
			PlotStyles = new ObservableCollection<string>(_queryService.GetPlotStyles());
			if (PlotStyles.Count > 0) SelectedPlotStyle = PlotStyles[0];

			// Base file name
			BaseFileName = _queryService.GetCurrentDrawingName();
			OutputPath = System.IO.Path.GetDirectoryName(
				System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ""));

			// Sort items
			SortOrderItems = new ObservableCollection<string>
			{
				_l10n.Translate("sort_lr"), _l10n.Translate("sort_rl"),
				_l10n.Translate("sort_tb"), _l10n.Translate("sort_bt"),
				_l10n.Translate("sort_sel"), _l10n.Translate("sort_mark"),
				_l10n.Translate("sort_none")
			};
			SelectedOrd1Index = 0; // Left to Right
			SelectedOrd2Index = 2; // Top to Bottom

			BasePointItems = new ObservableCollection<string>
			{
				_l10n.Translate("anchor_bl"), _l10n.Translate("anchor_br"),
				_l10n.Translate("anchor_tl"), _l10n.Translate("anchor_tr")
			};
			SelectedBasePointIndex = 0;
		}

		/// <summary>Khi printer thay đổi → cập nhật papers và file controls.</summary>
		partial void OnSelectedPrinterChanged(string value)
		{
			if (string.IsNullOrEmpty(value)) return;
			PaperSizes = new ObservableCollection<string>(_queryService.GetPaperSizes(value));
			if (PaperSizes.Count > 0) SelectedPaperSize = PaperSizes[0];
			IsFilePrinter = _queryService.IsFilePrinter(value);
		}

		/// <summary>Build PlotSettingsData từ ViewModel state.</summary>
		public PlotSettingsData BuildSettings()
		{
			return new PlotSettingsData
			{
				DeviceName = SelectedPrinter ?? "",
				PaperSize = SelectedPaperSize ?? "",
				PlotStyle = SelectedPlotStyle ?? "",
				OutputPath = OutputPath ?? "",
				BaseFileName = BaseFileName ?? "Drawing1",
				FrameType = IsBlockMode ? FrameType.Block : FrameType.Polyline,
				FrameNames = FrameNameList ?? new List<string>(),
				SelectionMode = IsManualSelect ? SelectionMode.Manual
					: SelectionMode.CurrentLayout,
				GroupOrder = IndexToSortOrder(SelectedOrd1Index),
				CrossGroupOrder = IndexToSortOrder(SelectedOrd2Index),
				SortBasePoint = (BasePoint)SelectedBasePointIndex,
				Orientation = Orientation,
				Fuzz = Fuzz,
				MarkPlotRegions = MarkPlotRegions,
				MergePdfs = MergePdfs,
				OpenPdf = OpenWhenDone,
				ConvertToImage = ConvertToImage,
				PdfEditor = PdfEditorEnabled,
				ImageFormat = ImageFormat,
				ImageDpi = ImageDpi,
				ManualSelectionHandles = ManualSelectionHandles
			};
		}

		/// <summary>Thực hiện plot chính.</summary>
		[RelayCommand]
		public void StartPlot()
		{
			if (IsPlotting) return;
			IsPlotting = true;
			StatusText = _l10n.Translate("prog_title");

			try
			{
				var settings = BuildSettings();
				var frames = _queryService.SelectFrames(settings);
				if (frames.Count == 0)
				{
					StatusText = _l10n.Translate("msg_no_result");
					IsPlotting = false;
					return;
				}

				_sortingService.SortFrames(frames, settings);
				FrameCount = frames.Count;

				if (settings.MarkPlotRegions)
					_markerService.DrawPermanentMarkers(frames);

				var progress = new Progress<PlotProgress>(p =>
				{
					StatusText = $"{p.CurrentPage}/{p.TotalPages}: {p.SubLabel}";
				});

				var result = _plotService.PlotAll(frames, settings, progress);

				// Post-processing
				if (result.IsFilePrinter && result.GeneratedFiles.Count > 0)
				{
					if (settings.Orientation != PlotOrientation.Auto)
						_pdfProcessor.ApplyOrientation(result.GeneratedFiles, settings.Orientation);

					if (settings.MergePdfs && result.GeneratedFiles.Count > 1)
					{
						result.FinalPath = _pdfProcessor.MergePdfs(result.GeneratedFiles, settings.OutputPath, settings.BaseFileName);
					}
					else if (settings.ConvertToImage)
					{
						var imageFiles = _pdfProcessor.ConvertToImages(result.GeneratedFiles, settings.ImageFormat, settings.ImageDpi);
						if (imageFiles.Count > 0) result.FinalPath = imageFiles[0];
					}

					if (settings.OpenPdf && result.FinalPath != null && System.IO.File.Exists(result.FinalPath))
					{
						try { System.Diagnostics.Process.Start(result.FinalPath); } catch { }
					}
				}

				StatusText = result.ErrorCount > 0
					? $"Done with {result.ErrorCount} error(s)"
					: $"Done! {result.TotalPages} page(s)";
			}
			catch (Exception ex)
			{
				StatusText = $"Error: {ex.Message}";
			}
			finally
			{
				IsPlotting = false;
			}
		}

		[RelayCommand]
		public void ClearMarkers()
		{
			_markerService.ClearPermanentMarkers();
		}

		// ─── Helpers ─────────────────────────────────────────────────────

		private static SortOrder IndexToSortOrder(int index)
		{
			return index switch
			{
				0 => SortOrder.LeftToRight,
				1 => SortOrder.RightToLeft,
				2 => SortOrder.TopToBottom,
				3 => SortOrder.BottomToTop,
				4 => SortOrder.SelectionOrder,
				5 => SortOrder.MarkedOrder,
				_ => SortOrder.None,
			};
		}
	}
}
