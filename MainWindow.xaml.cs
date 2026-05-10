using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
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
                    SubscribeDatabaseEvents();
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
                if (chkOpenPdf != null) chkOpenPdf.IsChecked = true;
            }
        }
        private void ChkConvertImage_Changed(object sender, RoutedEventArgs e)
        {
            if (isInitializing || chkConvertImage == null) return;
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
            if (isInitializing || chkPdfEditor == null) return;
            if (chkPdfEditor.IsChecked == true)
            {
                if (chkMergePdf != null) chkMergePdf.IsChecked = false;
                if (chkConvertImage != null) chkConvertImage.IsChecked = false;
                if (chkOpenPdf != null) chkOpenPdf.IsChecked = false;
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
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                // Tìm đường dẫn file CTB/STB
                string styleDir = "";
                using (doc.LockDocument())
                    styleDir = Path.Combine((string)Application.GetSystemVariable("ROAMABLEROOTPREFIX"), @"Plotters\Plot Styles");

                string path = Path.Combine(styleDir, styleName);

                // Fallback 1: thư mục chia sẻ qua biến hệ thống
                if (!File.Exists(path))
                {
                    try
                    {
                        string sharedDir = (string)Application.GetSystemVariable("PLOTSTYLEDIR");
                        if (!string.IsNullOrEmpty(sharedDir))
                        {
                            string altPath = Path.Combine(sharedDir, styleName);
                            if (File.Exists(altPath)) path = altPath;
                        }
                    }
                    catch { }
                }

                // Fallback 2: tìm styexe.exe trong thư mục cài đặt AutoCAD
                string acadDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                string styExePath = Path.Combine(acadDir, "styexe.exe");

                if (!File.Exists(path))
                {
                    System.Windows.MessageBox.Show(
                        $"Plot style file not found:\n\n" +
                        $"File: {styleName}\n" +
                        $"Searched in: {styleDir}",
                        "TPL — Edit Plot Style", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Ưu tiên dùng styexe.exe (trình chỉnh sửa CTB tích hợp của AutoCAD)
                // Nếu không tìm thấy styexe.exe, dùng UseShellExecute để Windows tự chọn ứng dụng
                if (File.Exists(styExePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = styExePath,
                        Arguments = "\"" + path + "\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Cannot open Plot Style Editor.\n\n" +
                    $"Style: {styleName}\n" +
                    $"Error: {ex.GetType().Name}: {ex.Message}",
                    "TPL — Edit Plot Style", MessageBoxButton.OK, MessageBoxImage.Error);
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
            data.ImageDpi = int.TryParse(txtDpi.Text, out int dpi) ? dpi : 600;
            data.BaseFileName = txtFileName.Text;
            if (rbOrientPortrait.IsChecked == true)
                data.Orientation = PlotHelper.PlotOrientation.Portrait;
            else if (rbOrientLandscape.IsChecked == true)
                data.Orientation = PlotHelper.PlotOrientation.Landscape;
            else
                data.Orientation = PlotHelper.PlotOrientation.Auto;
            data.ManualSelectionIds = new List<ObjectId>(tempManualSelectionIds);
            return data;
        }

        // ── Auto-Update Transients on Block Move ──
        private void SubscribeDatabaseEvents()
        {
            try
            {
                Application.DocumentManager.DocumentActivated -= DocumentManager_DocumentActivated_Local;
                Application.DocumentManager.DocumentActivated += DocumentManager_DocumentActivated_Local;

                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    AttachDbEvents(doc.Database);
                }
            }
            catch { }
        }

        private void UnsubscribeDatabaseEvents()
        {
            try
            {
                Application.DocumentManager.DocumentActivated -= DocumentManager_DocumentActivated_Local;
                foreach (Document doc in Application.DocumentManager)
                {
                    DetachDbEvents(doc.Database);
                }
            }
            catch { }
        }

        private void DocumentManager_DocumentActivated_Local(object sender, DocumentCollectionEventArgs e)
        {
            try
            {
                if (e.Document != null)
                {
                    AttachDbEvents(e.Document.Database);
                }
            }
            catch { }
        }

        private void AttachDbEvents(Database db)
        {
            if (db == null) return;
            DetachDbEvents(db); // Prevent double hook
            db.ObjectModified += Database_ObjectModified;
            db.ObjectErased += Database_ObjectErased;
            db.ObjectAppended += Database_ObjectModified;
        }

        private void DetachDbEvents(Database db)
        {
            if (db == null) return;
            db.ObjectModified -= Database_ObjectModified;
            db.ObjectErased -= Database_ObjectErased;
            db.ObjectAppended -= Database_ObjectModified;
        }

        private void Database_ObjectModified(object sender, ObjectEventArgs e)
        {
            if (isInitializing) return;
            try
            {
                if (e.DBObject is BlockReference || e.DBObject is Autodesk.AutoCAD.DatabaseServices.Polyline)
                {
                    this.Dispatcher.InvokeAsync(() => { TriggerPreviewInternal(); });
                }
            }
            catch { }
        }

        private void Database_ObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (isInitializing) return;
            try
            {
                if (e.DBObject is BlockReference || e.DBObject is Autodesk.AutoCAD.DatabaseServices.Polyline)
                {
                    this.Dispatcher.InvokeAsync(() => { TriggerPreviewInternal(); });
                }
            }
            catch { }
        }

        private void rbPng_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}
