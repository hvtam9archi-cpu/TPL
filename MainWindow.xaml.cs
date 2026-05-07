using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
    public partial class MainWindow : Window
    {
        public PlotHelper.PlotSettingsData Settings { get; private set; }
        public bool IsPlotConfirmed { get; private set; }

        private List<ObjectId> tempManualSelectionIds = new List<ObjectId>();
        private List<DBObject> transientObjects = new List<DBObject>();
        private bool isInitializing = true;
        private DispatcherTimer _previewDebounce;
        private Document _markerDoc;
        private Document _selectionDoc;
        private static Dictionary<Document, List<DBObject>> _pendingTransients = new Dictionary<Document, List<DBObject>>();
        private static bool _globalEventsSubscribed = false;

        public MainWindow()
        {
            try { L10n.Init(); } catch { }
            _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _previewDebounce.Tick += (s, e) => { _previewDebounce.Stop(); try { UpdatePreview(); } catch { } };

            if (!_globalEventsSubscribed)
            {
                try
                {
                    Application.DocumentManager.DocumentActivated += GlobalDocumentActivated;
                    Application.DocumentManager.DocumentToBeDestroyed += GlobalDocumentToBeDestroyed;
                    _globalEventsSubscribed = true;
                }
                catch { }
            }

            InitializeComponent();

            // Defer AutoCAD API calls — KHÔNG gọi trong constructor để tránh Access Violation
            this.Loaded += (s, e) =>
            {
                try
                {
                    LoadData();
                    isInitializing = false;
                    UpdatePreview();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TPL] LoadData error: {ex.Message}");
                    isInitializing = false;
                }
            };
        }

        // ── Title Bar ──
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        // ── Printer changed → reload papers ──
        private void CbPrinters_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cbPrinters.SelectedItem != null)
            {
                var papers = PlotHelper.GetPaperSizes(cbPrinters.SelectedItem.ToString());
                cbPapers.Items.Clear();
                foreach (var p in papers) cbPapers.Items.Add(p);
                if (cbPapers.Items.Count > 0) cbPapers.SelectedIndex = 0;
            }
        }

        // ── Mode changed (Block / Layer) ──
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

        // ── Mutual exclusion for output checkboxes ──
        private void ChkMergePdf_Changed(object sender, RoutedEventArgs e)
        {
            if (isInitializing || chkMergePdf == null) return;
            if (chkMergePdf.IsChecked == true) 
            { 
                if (chkConvertImage != null) chkConvertImage.IsChecked = false; 
                if (chkPdfEditor != null) chkPdfEditor.IsChecked = false; 
            }
        }
        private void ChkConvertImage_Changed(object sender, RoutedEventArgs e)
        {
            if (isInitializing || chkConvertImage == null) return;
            if (chkConvertImage.IsChecked == true) 
            { 
                if (chkMergePdf != null) chkMergePdf.IsChecked = false; 
                if (chkPdfEditor != null) chkPdfEditor.IsChecked = false; 
            }
            if (pnlImgFormat != null) pnlImgFormat.IsEnabled = chkConvertImage.IsChecked == true;
        }
        private void ChkPdfEditor_Changed(object sender, RoutedEventArgs e)
        {
            if (isInitializing || chkPdfEditor == null) return;
            if (chkPdfEditor.IsChecked == true) 
            { 
                if (chkMergePdf != null) chkMergePdf.IsChecked = false; 
                if (chkConvertImage != null) chkConvertImage.IsChecked = false; 
            }
        }

        // ── Preview triggers ──
        private void TriggerPreview(object sender, RoutedEventArgs e) => TriggerPreviewInternal();
        private void TriggerPreviewCombo(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => TriggerPreviewInternal();
        private void TriggerPreviewText(object sender, RoutedEventArgs e) => TriggerPreviewInternal();
        private void TriggerPreviewCheck(object sender, RoutedEventArgs e) => TriggerPreviewInternal();
        private void TriggerPreviewInternal()
        {
            if (isInitializing) return;
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        // ── Browse folder ──
        private void BtnBrowsePath_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    txtPath.Text = fbd.SelectedPath;
            }
        }

        // ── Edit style ──
        private void BtnEditStyle_Click(object sender, RoutedEventArgs e)
        {
            string styleName = cbStyles.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(styleName)) return;
            try
            {
                string styleDir = "";
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    using (doc.LockDocument())
                        styleDir = Path.Combine((string)Application.GetSystemVariable("ROAMABLEROOTPREFIX"), @"Plotters\Plot Styles");
                }
                string path = Path.Combine(styleDir, styleName);
                if (!File.Exists(path))
                {
                    string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Autodesk\AutoCAD 2021\R24.0\enu\Plotters\Plot Styles", styleName);
                    if (File.Exists(alt)) path = alt;
                }
                if (File.Exists(path))
                {
                    string acadDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    string styExe = Path.Combine(acadDir, "styexe.exe");
                    if (File.Exists(styExe))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = styExe, Arguments = "\"" + path + "\"", UseShellExecute = false });
                    else
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Delete marks ──
        private void BtnDeleteMarks_Click(object sender, RoutedEventArgs e)
        {
            chkMark.IsChecked = false;
            ClearPermanentMarkers();
        }

        // ── Back to editor ──
        private void BtnBackToEditor_Click(object sender, RoutedEventArgs e)
        {
            var pdfEditor = PdfEditorWindow.Instance;
            if (pdfEditor != null && pdfEditor.IsLoaded)
            {
                pdfEditor.Show();
                pdfEditor.Activate();
                this.Hide();
            }
        }

        // ── SubPlot mode ──
        public void SetSubPlotMode(bool isSubPlot)
        {
            chkMergePdf.IsEnabled = !isSubPlot;
            chkConvertImage.IsEnabled = !isSubPlot;
            chkPdfEditor.IsEnabled = !isSubPlot;
            btnBackToEditor.Visibility = isSubPlot ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (isSubPlot)
            {
                chkPdfEditor.IsChecked = true;
                chkMergePdf.IsChecked = false;
                chkConvertImage.IsChecked = false;
            }
        }

        // ── Build settings from UI ──
        private PlotHelper.PlotSettingsData BuildCurrentSettings()
        {
            var data = new PlotHelper.PlotSettingsData();
            data.DeviceName = cbPrinters.SelectedItem?.ToString();
            data.PaperSize = cbPapers.SelectedItem?.ToString();
            data.PlotStyle = cbStyles.SelectedItem?.ToString() ?? "";
            data.OutputPath = txtPath.Text;
            data.FrameType = rbBlockMode.IsChecked == true ? PlotHelper.FrameType.Block : PlotHelper.FrameType.Polyline;
            string namesStr = rbBlockMode.IsChecked == true ? txtBlocks.Text : txtLayers.Text;
            data.FrameNames = namesStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
            data.SelectionMode = rbAll.IsChecked == true ? PlotHelper.SelectionMode.AllLayouts : (rbSelect.IsChecked == true ? PlotHelper.SelectionMode.Manual : PlotHelper.SelectionMode.CurrentLayout);
            data.GroupOrder = (PlotHelper.SortOrder)(cbOrd1.SelectedIndex >= 0 ? cbOrd1.SelectedIndex : 0);
            int o2 = cbOrd2.SelectedIndex >= 0 ? cbOrd2.SelectedIndex : 0;
            data.CrossGroupOrder = (PlotHelper.SortOrder)(o2 == 0 ? 6 : o2 - 1);
            data.SortBasePoint = (PlotHelper.BasePoint)(cbBase.SelectedIndex >= 0 ? cbBase.SelectedIndex : 0);
            data.Fuzz = double.TryParse(txtFuzz.Text, out double f) ? f : 100;
            data.MarkPlotRegions = chkMark.IsChecked == true;
            data.MergePdfs = chkMergePdf.IsChecked == true;
            data.OpenPdf = chkOpenPdf.IsChecked == true;
            data.ConvertToImage = chkConvertImage.IsChecked == true;
            data.PdfEditor = chkPdfEditor.IsChecked == true;
            data.ImageFormat = rbJpg.IsChecked == true ? "JPG" : "PNG";
            data.ImageDpi = int.TryParse(txtDpi.Text, out int dpi) ? dpi : 300;
            data.BaseFileName = txtFileName.Text;
            data.ManualSelectionIds = new List<ObjectId>(tempManualSelectionIds);
            return data;
        }

		private void rbPng_Checked(object sender, RoutedEventArgs e)
		{

		}
	}
}
