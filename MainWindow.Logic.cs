using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
    public partial class MainWindow
    {
        // ── Load Data ──
        private void LoadData()
        {
            foreach (var p in PlotHelper.GetPrinters()) cbPrinters.Items.Add(p);
            foreach (var s in PlotHelper.GetPlotStyles()) cbStyles.Items.Add(s);

            cbOrd1.Items.Add(L10n.T("sort_lr")); cbOrd1.Items.Add(L10n.T("sort_rl"));
            cbOrd1.Items.Add(L10n.T("sort_tb")); cbOrd1.Items.Add(L10n.T("sort_bt"));
            cbOrd1.Items.Add(L10n.T("sort_sel")); cbOrd1.Items.Add(L10n.T("sort_mark"));
            cbOrd1.SelectedIndex = 0;

            cbOrd2.Items.Add(L10n.T("sort_none")); cbOrd2.Items.Add(L10n.T("sort_lr"));
            cbOrd2.Items.Add(L10n.T("sort_rl")); cbOrd2.Items.Add(L10n.T("sort_tb"));
            cbOrd2.Items.Add(L10n.T("sort_bt"));
            cbOrd2.SelectedIndex = 3;

            cbBase.Items.Add(L10n.T("anchor_bl")); cbBase.Items.Add(L10n.T("anchor_br"));
            cbBase.Items.Add(L10n.T("anchor_tl")); cbBase.Items.Add(L10n.T("anchor_tr"));
            cbBase.SelectedIndex = 0;

            if (Commands.LastSettings != null)
            {
                var ls = Commands.LastSettings;
                SelectComboItem(cbPrinters, ls.DeviceName);
                SelectComboItem(cbPapers, ls.PaperSize);
                SelectComboItem(cbStyles, ls.PlotStyle);
                txtPath.Text = ls.OutputPath;
                if (ls.FrameType == PlotHelper.FrameType.Block) { rbBlockMode.IsChecked = true; txtBlocks.Text = string.Join(", ", ls.FrameNames); }
                else { rbLayerMode.IsChecked = true; txtLayers.Text = string.Join(", ", ls.FrameNames); }
                rbAll.IsChecked = ls.SelectionMode == PlotHelper.SelectionMode.AllLayouts;
                rbCurrent.IsChecked = ls.SelectionMode == PlotHelper.SelectionMode.CurrentLayout;
                rbSelect.IsChecked = ls.SelectionMode == PlotHelper.SelectionMode.Manual;
                cbOrd1.SelectedIndex = (int)ls.GroupOrder;
                cbOrd2.SelectedIndex = ls.CrossGroupOrder == PlotHelper.SortOrder.None ? 0 : (int)ls.CrossGroupOrder + 1;
                cbBase.SelectedIndex = (int)ls.SortBasePoint;
                txtFuzz.Text = ls.Fuzz.ToString();
                chkMark.IsChecked = ls.MarkPlotRegions;
                chkMergePdf.IsChecked = ls.MergePdfs;
                chkOpenPdf.IsChecked = ls.OpenPdf;
                chkConvertImage.IsChecked = ls.ConvertToImage;
                chkPdfEditor.IsChecked = ls.PdfEditor;
                if (ls.ImageFormat == "JPG") rbJpg.IsChecked = true; else rbPng.IsChecked = true;
                txtDpi.Text = ls.ImageDpi.ToString();
                txtFileName.Text = ls.BaseFileName;
            }
            else
            {
                var activeDoc = Application.DocumentManager.MdiActiveDocument;
                txtFileName.Text = activeDoc != null ? Path.GetFileNameWithoutExtension(activeDoc.Name) : "output";
                SelectComboItem(cbPrinters, "AutoCAD PDF (High Quality Print).pc3");
                SelectComboItem(cbStyles, "monochrome.ctb");
                SelectComboItem(cbPapers, "ISO full bleed A3 (420.00 x 297.00 MM)");
                txtPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
        }

        private void SelectComboItem(System.Windows.Controls.ComboBox cb, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < cb.Items.Count; i++)
                if (string.Equals(cb.Items[i].ToString(), value, StringComparison.OrdinalIgnoreCase))
                { cb.SelectedIndex = i; return; }
        }

        // ── Preview ──
        private void UpdatePreview()
        {
            try
            {
                Document activDoc = Application.DocumentManager?.MdiActiveDocument;
                if (activDoc == null || activDoc.IsDisposed) return;
                if (_selectionDoc != null && _selectionDoc != activDoc)
                {
                    tempManualSelectionIds.Clear(); txtBlocks.Text = ""; txtLayers.Text = "";
                    _selectionDoc = null;
                    try { ClearTransientMarkers(); } catch { }
                    try { ClearPermanentMarkers(); } catch { }
                }
                bool hasTemplate = rbSelect.IsChecked == true
                    ? tempManualSelectionIds.Count > 0
                    : (rbBlockMode.IsChecked == true ? txtBlocks.Text : txtLayers.Text).Trim().Length > 0;
                if (!hasTemplate)
                {
                    lblCount.Text = L10n.T("msg_no_frame");
                    lblCount.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    try { ClearTransientMarkers(); } catch { }
                    try { ClearPermanentMarkers(); } catch { }
                }
                else
                {
                    var sm = BuildCurrentSettings();
                    var frames = PlotLogic.SelectFrames(sm);
                    PlotLogic.SortFrames(frames, sm);
                    lblCount.Text = rbSelect.IsChecked == true
                        ? string.Format("{0}: {1}", L10n.T("rb_manual"), tempManualSelectionIds.Count)
                        : string.Format("{0}: {1}", L10n.T("header_frame"), frames.Count);
                    lblCount.Foreground = frames.Count > 0
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    try { DrawMarkersIfNeeded(frames); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TPL] UpdatePreview error: {ex.Message}");
            }
        }

        // ── Select Block ──
        private void BtnSelectBlock_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            try
            {
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null || doc.IsDisposed) return;
                Editor ed = doc.Editor;
                var pso = new PromptSelectionOptions { MessageForAdding = L10n.T("msg_sel_block") };
                var filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                var psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    tempManualSelectionIds.Clear();
                    try { ClearTransientMarkers(); ClearPermanentMarkers(); } catch { }
                    var names = new HashSet<string>();
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                            string n = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
                            names.Add(n);
                        }
                    }
                    txtBlocks.Text = string.Join(", ", names);
                    rbBlockMode.IsChecked = true;
                    _selectionDoc = doc;
                    UpdatePreview();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] SelectBlock error: {ex.Message}"); }
            finally { this.Show(); }
        }

        // ── Select Layer ──
        private void BtnSelectLayer_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            try
            {
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null || doc.IsDisposed) return;
                Editor ed = doc.Editor;
                var pso = new PromptSelectionOptions { MessageForAdding = L10n.T("msg_sel_layer") };
                var filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
                var psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    tempManualSelectionIds.Clear();
                    try { ClearTransientMarkers(); ClearPermanentMarkers(); } catch { }
                    var names = new HashSet<string>();
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                            names.Add(ent.Layer);
                        }
                    }
                    txtLayers.Text = string.Join(", ", names);
                    rbLayerMode.IsChecked = true;
                    _selectionDoc = doc;
                    UpdatePreview();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] SelectLayer error: {ex.Message}"); }
            finally { this.Show(); }
        }

        // ── Select Manual ──
        private void BtnDeleteManual_Click(object sender, RoutedEventArgs e)
        {
            tempManualSelectionIds.Clear();
            _selectionDoc = null;
            try { ClearPermanentMarkers(); } catch { }
            UpdatePreview();
        }

        private void BtnSelectManual_Click(object sender, RoutedEventArgs e)
        {
            var ds = BuildCurrentSettings();
            if (ds.FrameNames.Count == 0)
            {
                System.Windows.MessageBox.Show(L10n.T("msg_need_sample"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Document doc = null;
            this.Hide();
            try
            {
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null || doc.IsDisposed) return;
                Editor ed = doc.Editor;
                var filter = new SelectionFilter(PlotLogic.GetFilter(ds));
                var pso = new PromptSelectionOptions { MessageForAdding = L10n.T("msg_sel_frames") };
                var psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    tempManualSelectionIds.Clear();
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            if (ds.FrameType == PlotHelper.FrameType.Block)
                            {
                                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                                string name = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
                                if (ds.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase)))
                                    tempManualSelectionIds.Add(id);
                            }
                            else tempManualSelectionIds.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] SelectManual error: {ex.Message}"); }
            finally { this.Show(); }
            rbSelect.IsChecked = true;
            _selectionDoc = doc;
            try { UpdatePreview(); } catch { }
        }

        // ── Plot ──
        private void BtnPlot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Settings = BuildCurrentSettings();
                if (Settings.FrameNames.Count == 0)
                { System.Windows.MessageBox.Show(L10n.T("msg_no_frame"), L10n.T("err_title"), MessageBoxButton.OK, MessageBoxImage.Error); return; }
                if (Settings.SelectionMode == PlotHelper.SelectionMode.Manual && Settings.ManualSelectionIds.Count == 0)
                { System.Windows.MessageBox.Show(L10n.T("msg_no_manual"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                IsPlotConfirmed = true;
                Commands.LastSettings = Settings;
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null || doc.IsDisposed) return;
                List<PlotFrame> frames = PlotLogic.SelectFrames(Settings);
                if (frames.Count == 0)
                { System.Windows.MessageBox.Show(L10n.T("msg_no_result"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
                PlotLogic.SortFrames(frames, Settings);
                using (DocumentLock docLock = doc.LockDocument())
                    PlotLogic.PlotAll(frames, Settings);
            }
            catch (Exception ex)
            { System.Windows.MessageBox.Show(string.Format(L10n.T("msg_plot_error"), ex.Message), L10n.T("err_title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
