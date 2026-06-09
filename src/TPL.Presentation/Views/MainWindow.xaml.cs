using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;
using TPL.Presentation.ViewModels;

namespace TPL.Presentation.Views
{
	/// <summary>
	/// MainWindow code-behind — chứa UI events và AutoCAD interaction callbacks.
	/// Logic thuần delegate qua MainWindowViewModel.
	/// AutoCAD API calls được thực hiện qua callback Action delegates từ host (TPL.App).
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly MainWindowViewModel _vm;
		private bool _isInitializing = true;
		private readonly DispatcherTimer _previewDebounce;

		// ── Callbacks for AutoCAD API (set by host/Commands.cs) ──
		public Action<MainWindow> OnSelectBlockRequested { get; set; }
		public Action<MainWindow> OnSelectLayerRequested { get; set; }
		public Action<MainWindow> OnSelectManualRequested { get; set; }
		public Action<MainWindow> OnAddManualRequested { get; set; }
		public Action<MainWindow> OnRemoveManualRequested { get; set; }
		public Action<MainWindow> OnBrowsePathRequested { get; set; }
		public Action<MainWindow> OnEditStyleRequested { get; set; }
		public Action<MainWindow> OnPlotRequested { get; set; }
		public Action OnDeleteMarksRequested { get; set; }
		public Action OnBackToEditorRequested { get; set; }
		public Func<PlotSettingsData, List<PlotFrame>> OnPreviewRequested { get; set; }

		public MainWindow(MainWindowViewModel vm)
		{
			_vm = vm;
			DataContext = _vm;

			_previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
			_previewDebounce.Tick += (s, e) => { _previewDebounce.Stop(); try { UpdatePreview(); } catch { } };

			InitializeComponent();

			this.Loaded += (s, e) =>
			{
				try
				{
					_vm.LoadData();
					LoadComboBoxes();
					_isInitializing = false;
					UpdateOpenPdfState();
					UpdatePreview();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[TPL] LoadData error: {ex.Message}");
					_isInitializing = false;
				}
			};
		}

		// ── Load ComboBoxes from ViewModel ──
		private void LoadComboBoxes()
		{
			foreach (var p in _vm.Printers) cbPrinters.Items.Add(p);
			foreach (var s in _vm.PlotStyles) cbStyles.Items.Add(s);

			foreach (var item in _vm.SortOrderItems)
			{
				cbOrd1.Items.Add(item);
				if (item != _vm.SortOrderItems[_vm.SortOrderItems.Count - 1]) // Skip "None" for Ord2
					cbOrd2.Items.Add(item);
			}
			// Add None as first item for Ord2
			cbOrd2.Items.Insert(0, _vm.SortOrderItems[_vm.SortOrderItems.Count - 1]);

			foreach (var item in _vm.BasePointItems) cbBase.Items.Add(item);

			if (cbPrinters.Items.Count > 0) cbPrinters.SelectedIndex = 0;
			if (cbStyles.Items.Count > 0) cbStyles.SelectedIndex = 0;
			cbOrd1.SelectedIndex = 0;
			cbOrd2.SelectedIndex = 3;
			cbBase.SelectedIndex = 0;

			txtFileName.Text = _vm.BaseFileName;
			txtPath.Text = _vm.OutputPath;
		}

		// ── Title Bar ──
		private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
		private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
		private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

		// ── Printer changed ──
		private void CbPrinters_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (cbPrinters.SelectedItem == null) return;
			_vm.SelectedPrinter = cbPrinters.SelectedItem.ToString();
			cbPapers.Items.Clear();
			foreach (var p in _vm.PaperSizes) cbPapers.Items.Add(p);
			if (cbPapers.Items.Count > 0) cbPapers.SelectedIndex = 0;
			UpdateFileOutputControls(_vm.IsFilePrinter);
		}

		private void UpdateFileOutputControls(bool isFilePrinter)
		{
			if (txtFileName != null) txtFileName.IsEnabled = isFilePrinter;
			if (txtPath != null) txtPath.IsEnabled = isFilePrinter;
			if (btnBrowsePath != null) btnBrowsePath.IsEnabled = isFilePrinter;
			if (lblFileName != null) lblFileName.IsEnabled = isFilePrinter;
			if (lblFolder != null) lblFolder.IsEnabled = isFilePrinter;
			if (chkMergePdf != null) { chkMergePdf.IsEnabled = isFilePrinter; if (!isFilePrinter) chkMergePdf.IsChecked = false; }
			if (chkPdfEditor != null) { chkPdfEditor.IsEnabled = isFilePrinter; if (!isFilePrinter) chkPdfEditor.IsChecked = false; }
			UpdateOpenPdfState();
			if (chkConvertImage != null) { chkConvertImage.IsEnabled = isFilePrinter; if (!isFilePrinter) chkConvertImage.IsChecked = false; }
			if (pnlImgFormat != null) pnlImgFormat.IsEnabled = isFilePrinter && chkConvertImage.IsChecked == true;
			if (rbOrientAuto != null) rbOrientAuto.IsEnabled = isFilePrinter;
			if (rbOrientPortrait != null) rbOrientPortrait.IsEnabled = isFilePrinter;
			if (rbOrientLandscape != null) rbOrientLandscape.IsEnabled = isFilePrinter;
			if (!isFilePrinter && rbOrientAuto != null) rbOrientAuto.IsChecked = true;
		}

		private void UpdateOpenPdfState()
		{
			if (chkOpenPdf == null) return;
			bool shouldEnable = _vm.IsFilePrinter && (chkPdfEditor == null || chkPdfEditor.IsChecked != true);
			chkOpenPdf.IsEnabled = shouldEnable;
			if (!shouldEnable) chkOpenPdf.IsChecked = false;
		}

		// ── Mode changed ──
		private void ModeChanged(object sender, RoutedEventArgs e)
		{
			if (txtBlocks == null) return;
			bool isBlock = rbBlockMode.IsChecked == true;
			txtBlocks.IsEnabled = isBlock;
			btnSelectBlock.IsEnabled = isBlock;
			txtLayers.IsEnabled = !isBlock;
			btnSelectLayer.IsEnabled = !isBlock;
			TriggerPreviewInternal();
		}

		// ── Mutual exclusion checkboxes ──
		private void ChkMergePdf_Changed(object sender, RoutedEventArgs e)
		{
			if (_isInitializing || chkMergePdf == null) return;
			if (chkMergePdf.IsChecked == true)
			{
				if (chkConvertImage != null) chkConvertImage.IsChecked = false;
				if (chkPdfEditor != null) chkPdfEditor.IsChecked = false;
				if (chkOpenPdf != null) chkOpenPdf.IsChecked = true;
			}
		}
		private void ChkConvertImage_Changed(object sender, RoutedEventArgs e)
		{
			if (_isInitializing || chkConvertImage == null) return;
			if (chkConvertImage.IsChecked == true)
			{
				if (chkMergePdf != null) chkMergePdf.IsChecked = false;
				if (chkPdfEditor != null) chkPdfEditor.IsChecked = false;
				if (chkOpenPdf != null) chkOpenPdf.IsChecked = false;
			}
			if (pnlImgFormat != null) pnlImgFormat.IsEnabled = chkConvertImage.IsChecked == true;
		}
		private void ChkPdfEditor_Changed(object sender, RoutedEventArgs e)
		{
			if (_isInitializing || chkPdfEditor == null) return;
			if (chkPdfEditor.IsChecked == true)
			{
				if (chkMergePdf != null) chkMergePdf.IsChecked = false;
				if (chkConvertImage != null) chkConvertImage.IsChecked = false;
			}
			UpdateOpenPdfState();
		}

		// ── Preview triggers ──
		private void TriggerPreview(object sender, RoutedEventArgs e) => TriggerPreviewInternal();
		private void TriggerPreviewCombo(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => TriggerPreviewInternal();
		private void TriggerPreviewText(object sender, RoutedEventArgs e) => TriggerPreviewInternal();
		private void TriggerPreviewCheck(object sender, RoutedEventArgs e) => TriggerPreviewInternal();
		private void TriggerPreviewInternal()
		{
			if (_isInitializing) return;
			_previewDebounce.Stop();
			_previewDebounce.Start();
		}

		// ── Delegate to host callbacks ──
		private void BtnSelectBlock_Click(object sender, RoutedEventArgs e) => OnSelectBlockRequested?.Invoke(this);
		private void BtnSelectLayer_Click(object sender, RoutedEventArgs e) => OnSelectLayerRequested?.Invoke(this);
		private void BtnSelectManual_Click(object sender, RoutedEventArgs e) => OnSelectManualRequested?.Invoke(this);
		private void BtnAddManual_Click(object sender, RoutedEventArgs e) => OnAddManualRequested?.Invoke(this);
		private void BtnRemoveManual_Click(object sender, RoutedEventArgs e) => OnRemoveManualRequested?.Invoke(this);
		private void BtnBrowsePath_Click(object sender, RoutedEventArgs e) => OnBrowsePathRequested?.Invoke(this);
		private void BtnEditStyle_Click(object sender, RoutedEventArgs e) => OnEditStyleRequested?.Invoke(this);
		private void BtnDeleteMarks_Click(object sender, RoutedEventArgs e)
		{
			chkMark.IsChecked = false;
			OnDeleteMarksRequested?.Invoke();
		}
		private void BtnBackToEditor_Click(object sender, RoutedEventArgs e) => OnBackToEditorRequested?.Invoke();
		private void BtnPlot_Click(object sender, RoutedEventArgs e) => OnPlotRequested?.Invoke(this);

		// ── Preview update ──
		private void UpdatePreview()
		{
			try
			{
				SyncViewModelFromUI();
				var settings = _vm.BuildSettings();
				var frames = OnPreviewRequested?.Invoke(settings);
				if (frames != null && frames.Count > 0)
				{
					lblCount.Text = $"Frames: {frames.Count}";
					lblCount.Foreground = new System.Windows.Media.SolidColorBrush(
						System.Windows.Media.Color.FromRgb(34, 197, 94));
				}
				else
				{
					lblCount.Text = "No frames found";
					lblCount.Foreground = new System.Windows.Media.SolidColorBrush(
						System.Windows.Media.Color.FromRgb(239, 68, 68));
				}
			}
			catch { }
		}

		// ── Sync UI → ViewModel ──
		public void SyncViewModelFromUI()
		{
			_vm.SelectedPrinter = cbPrinters.SelectedItem?.ToString();
			_vm.SelectedPaperSize = cbPapers.SelectedItem?.ToString();
			_vm.SelectedPlotStyle = cbStyles.SelectedItem?.ToString();
			_vm.OutputPath = txtPath.Text;
			_vm.BaseFileName = txtFileName.Text;
			_vm.IsBlockMode = rbBlockMode.IsChecked == true;
			string namesStr = _vm.IsBlockMode ? txtBlocks.Text : txtLayers.Text;
			_vm.FrameNameList = namesStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(n => n.Trim()).ToList();
			_vm.IsAutoSelect = rbCurrent.IsChecked == true;
			_vm.IsManualSelect = rbSelect.IsChecked == true;
			_vm.SelectedOrd1Index = cbOrd1.SelectedIndex >= 0 ? cbOrd1.SelectedIndex : 0;
			_vm.SelectedOrd2Index = cbOrd2.SelectedIndex >= 0 ? cbOrd2.SelectedIndex : 0;
			_vm.SelectedBasePointIndex = cbBase.SelectedIndex >= 0 ? cbBase.SelectedIndex : 0;
			_vm.Fuzz = double.TryParse(txtFuzz.Text, out double f) ? f : 100;
			_vm.MarkPlotRegions = chkMark.IsChecked == true;
			_vm.MergePdfs = chkMergePdf.IsChecked == true;
			_vm.OpenWhenDone = chkOpenPdf.IsChecked == true;
			_vm.ConvertToImage = chkConvertImage.IsChecked == true;
			_vm.PdfEditorEnabled = chkPdfEditor.IsChecked == true;
			_vm.ImageFormat = rbJpg.IsChecked == true ? "JPG" : "PNG";
			_vm.ImageDpi = int.TryParse(txtDpi.Text, out int dpi) ? dpi : 600;

			if (rbOrientPortrait.IsChecked == true)
				_vm.Orientation = Domain.Enums.PlotOrientation.Portrait;
			else if (rbOrientLandscape.IsChecked == true)
				_vm.Orientation = Domain.Enums.PlotOrientation.Landscape;
			else
				_vm.Orientation = Domain.Enums.PlotOrientation.Auto;
		}

		// ── Public accessors for host to set UI state ──
		public void SetBlockNames(string names) { txtBlocks.Text = names; rbBlockMode.IsChecked = true; }
		public void SetLayerNames(string names) { txtLayers.Text = names; rbLayerMode.IsChecked = true; }
		public void SetManualMode() { rbSelect.IsChecked = true; }
		public void RefreshPreview() => TriggerPreviewInternal();

		public void SetSubPlotMode(bool isSubPlot)
		{
			chkMergePdf.IsEnabled = !isSubPlot;
			chkConvertImage.IsEnabled = !isSubPlot;
			chkPdfEditor.IsEnabled = !isSubPlot;
			btnBackToEditor.Visibility = isSubPlot ? Visibility.Visible : Visibility.Collapsed;
			if (isSubPlot)
			{
				chkPdfEditor.IsChecked = true;
				chkMergePdf.IsChecked = false;
				chkConvertImage.IsChecked = false;
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			_previewDebounce?.Stop();
			base.OnClosed(e);
		}
	}
}
