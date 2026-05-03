using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TPL
{
    public class PlotFrame
    {
        public ObjectId Id { get; set; }
        public Extents3d Extents { get; set; }
        public string LayoutName { get; set; }
        public int OrderIndex { get; set; }
        public string MarkerText { get; set; }

        public Point3d GetBasePoint(PlotHelper.BasePoint bpType)
        {
            switch (bpType)
            {
                case PlotHelper.BasePoint.BottomLeft: return new Point3d(Extents.MinPoint.X, Extents.MinPoint.Y, 0);
                case PlotHelper.BasePoint.BottomRight: return new Point3d(Extents.MaxPoint.X, Extents.MinPoint.Y, 0);
                case PlotHelper.BasePoint.TopLeft: return new Point3d(Extents.MinPoint.X, Extents.MaxPoint.Y, 0);
                case PlotHelper.BasePoint.TopRight: return new Point3d(Extents.MaxPoint.X, Extents.MaxPoint.Y, 0);
                default: return Extents.MinPoint;
            }
        }
    }

    public static class PlotLogic
    {
        public static List<PlotFrame> SelectFrames(PlotHelper.PlotSettingsData settings)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<PlotFrame> frames = new List<PlotFrame>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (settings.SelectionMode == PlotHelper.SelectionMode.Manual)
                {
                    if (settings.ManualSelectionIds != null)
                    {
                        foreach (ObjectId id in settings.ManualSelectionIds)
                        {
                            if (id.IsErased || id.IsNull) continue;
                            AddFrame(frames, tr, id, settings);
                        }
                    }
                }
                else
                {
                    string currentLayout = LayoutManager.Current.CurrentLayout;
                    TypedValue[] filter = GetFilter(settings);
                    SelectionFilter selFilter = new SelectionFilter(filter);
                    PromptSelectionResult psr = ed.SelectAll(selFilter);

                    if (psr.Status == PromptStatus.OK)
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            if (id.IsErased || id.IsNull) continue;
                            Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead, false, true);
                            if (ent == null || ent.IsErased) continue;
                            string entLayout = "";
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ent.OwnerId, OpenMode.ForRead);
                            if (btr.IsLayout)
                            {
                                Layout lay = (Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead);
                                entLayout = lay.LayoutName;
                            }

                            if (settings.SelectionMode == PlotHelper.SelectionMode.CurrentLayout && entLayout != currentLayout)
                                continue;

                            AddFrame(frames, tr, id, settings, entLayout);
                        }
                    }
                }
                tr.Commit();
            }

            return frames;
        }

        public static TypedValue[] GetFilter(PlotHelper.PlotSettingsData settings)
        {
            if (settings.FrameType == PlotHelper.FrameType.Block)
            {
                return new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
            }
            else
            {
                string layerNames = string.Join(",", settings.FrameNames);
                if (string.IsNullOrEmpty(layerNames)) layerNames = "*";
                return new TypedValue[] {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.LayerName, layerNames)
                };
            }
        }

        private static void AddFrame(List<PlotFrame> frames, Transaction tr, ObjectId id, PlotHelper.PlotSettingsData settings, string layoutName = "")
        {
            if (id.IsErased || id.IsNull) return;
            Entity ent = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
            if (ent == null || ent.IsErased) return;

            // FastSetVisibility pattern: skip hidden / layer-off / layer-frozen objects
            if (!ent.Visible) return;
            try
            {
                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
                if (layer.IsOff || layer.IsFrozen) return;
            }
            catch { }

            if (settings.FrameType == PlotHelper.FrameType.Block)
            {
                BlockReference br = ent as BlockReference;
                if (br != null)
                {
                    string name = GetBlockName(br, tr);
                    // Match any of the allowed block names (case insensitive)
                    if (settings.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr);
                        frames.Add(new PlotFrame { Id = id, Extents = br.GeometricExtents, LayoutName = layoutName });
                    }
                }
            }
            else
            {
                Polyline pl = ent as Polyline;
                if (pl != null)
                {
                    if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr);
                    frames.Add(new PlotFrame { Id = id, Extents = pl.GeometricExtents, LayoutName = layoutName });
                }
            }
        }

        private static string GetBlockName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            return br.Name;
        }

        private static string GetLayoutName(ObjectId ownerId, Transaction tr)
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ownerId, OpenMode.ForRead);
            if (btr.IsLayout)
            {
                Layout lay = (Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead);
                return lay.LayoutName;
            }
            return "Model";
        }

        public static void SortFrames(List<PlotFrame> frames, PlotHelper.PlotSettingsData settings)
        {
            if (settings.GroupOrder == PlotHelper.SortOrder.SelectionOrder || settings.GroupOrder == PlotHelper.SortOrder.None)
                return;

            if (settings.GroupOrder == PlotHelper.SortOrder.MarkedOrder)
                return;

            var ord1 = settings.GroupOrder;
            var ord2 = settings.CrossGroupOrder;
            var bp = settings.SortBasePoint;
            double fuzz = settings.Fuzz;

            frames.Sort((f1, f2) =>
            {
                Point3d p1 = f1.GetBasePoint(bp);
                Point3d p2 = f2.GetBasePoint(bp);

                if (ord2 != PlotHelper.SortOrder.None)
                {
                    if (ord1 == PlotHelper.SortOrder.LeftToRight || ord1 == PlotHelper.SortOrder.RightToLeft)
                    {
                        if (Math.Abs(p1.Y - p2.Y) <= fuzz)
                            return (ord1 == PlotHelper.SortOrder.LeftToRight) ? p1.X.CompareTo(p2.X) : p2.X.CompareTo(p1.X);
                        return (ord2 == PlotHelper.SortOrder.TopToBottom) ? p2.Y.CompareTo(p1.Y) : p1.Y.CompareTo(p2.Y);
                    }
                    else
                    {
                        if (Math.Abs(p1.X - p2.X) <= fuzz)
                            return (ord1 == PlotHelper.SortOrder.TopToBottom) ? p2.Y.CompareTo(p1.Y) : p1.Y.CompareTo(p2.Y);
                        return (ord2 == PlotHelper.SortOrder.LeftToRight) ? p1.X.CompareTo(p2.X) : p2.X.CompareTo(p1.X);
                    }
                }
                else
                {
                    if (ord1 == PlotHelper.SortOrder.TopToBottom) return p2.Y.CompareTo(p1.Y);
                    if (ord1 == PlotHelper.SortOrder.BottomToTop) return p1.Y.CompareTo(p2.Y);
                    if (ord1 == PlotHelper.SortOrder.LeftToRight) return p1.X.CompareTo(p2.X);
                    if (ord1 == PlotHelper.SortOrder.RightToLeft) return p2.X.CompareTo(p1.X);
                    return 0;
                }
            });
        }

        public static void PlotAll(List<PlotFrame> frames, PlotHelper.PlotSettingsData settings)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            if (frames.Count == 0) { ed.WriteMessage("\nNo plot frames found."); return; }

            string baseName = settings.BaseFileName;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Drawing1";
            string ext = settings.DeviceName.ToLower().Contains("pdf") ? ".pdf" : ".plt";
            string outDir = settings.OutputPath;
            if (!outDir.EndsWith("\\")) outDir += "\\";
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // Phase 1: pre-collect all layout/plot data inside ONE transaction
            // PlotEngine must NEVER run while a transaction is active.
            var plotJobs = new List<(string LayoutName, ObjectId LayoutId, bool ModelType, PlotSettings Ps, Extents3d Extents)>();
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var frame in frames)
                    {
                        ObjectId layId = LayoutManager.Current.GetLayoutId(frame.LayoutName);
                        Layout lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                        PlotSettings ps = new PlotSettings(lay.ModelType);
                        ps.CopyFrom(lay);
                        PlotSettingsValidator psv = PlotSettingsValidator.Current;

                        // CRITICAL: Must set PlotWindowArea (even dummy WCS) BEFORE setting PlotType to Window to avoid eInvalidInput
                        psv.SetPlotWindowArea(ps, new Extents2d(frame.Extents.MinPoint.X, frame.Extents.MinPoint.Y, frame.Extents.MaxPoint.X, frame.Extents.MaxPoint.Y));
                        psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);

                        try { psv.SetPlotConfigurationName(ps, settings.DeviceName, settings.PaperSize.Replace(" ", "_")); } catch { }
                        try { psv.SetCurrentStyleSheet(ps, settings.PlotStyle); } catch { }

                        psv.SetUseStandardScale(ps, true);
                        psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                        psv.SetPlotCentered(ps, true);

                        // CRITICAL: Force standard rendering to avoid blank PDFs from custom visual styles
                        ps.PrintLineweights = true;
                        ps.PlotPlotStyles = true;
                        ps.DrawViewportsFirst = true;
                        ps.PlotHidden = false;

                        double lenX = frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X;
                        double lenY = frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y;
                        psv.SetPlotRotation(ps, lenX > lenY ? PlotRotation.Degrees000 : PlotRotation.Degrees090);

                        plotJobs.Add((frame.LayoutName, layId, lay.ModelType, ps, frame.Extents));
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPlot data error: {ex.Message}");
                return;
            }

            // Phase 2: PlotEngine + Regen with NO active transaction
            short bgPlot = (short)Application.GetSystemVariable("BACKGROUNDPLOT");
            Application.SetSystemVariable("BACKGROUNDPLOT", 0);

            var progressForm = new System.Windows.Forms.Form();
            progressForm.Text = L10n.T("prog_title");
            progressForm.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            progressForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            progressForm.ClientSize = new System.Drawing.Size(380, 90);
            progressForm.MaximizeBox = false; progressForm.MinimizeBox = false; progressForm.ControlBox = false;
            var lblProg = new System.Windows.Forms.Label { Left = 10, Top = 10, Width = 360, Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(0, 102, 204) };
            var pb = new System.Windows.Forms.ProgressBar { Left = 10, Top = 38, Width = 360, Height = 22, Minimum = 0, Maximum = plotJobs.Count, Style = System.Windows.Forms.ProgressBarStyle.Continuous };
            var lblSub = new System.Windows.Forms.Label { Left = 10, Top = 65, Width = 360, ForeColor = System.Drawing.Color.DimGray, Font = new System.Drawing.Font("Segoe UI", 8.5F) };
            progressForm.Controls.Add(lblProg); progressForm.Controls.Add(pb); progressForm.Controls.Add(lblSub);
            Application.ShowModelessDialog(Application.MainWindow.Handle, progressForm);

            var generatedFiles = new List<string>();
            try
            {
                for (int i = 0; i < plotJobs.Count; i++)
                {
                    var job = plotJobs[i];
                    string filePath = Path.Combine(outDir, $"{baseName}_{i + 1:D2}{ext}");

                    lblProg.Text = string.Format(L10n.T("prog_progress"), i + 1, plotJobs.Count);
                    pb.Value = i;
                    lblSub.Text = string.Format(L10n.T("prog_file"), $"{baseName}_{i + 1:D2}{ext}");
                    progressForm.Update();

                    // Layout switch + Zoom to Window + Regen
                    // Zooming ensures graphics pipeline doesn't cull objects outside current view
                    LayoutManager.Current.CurrentLayout = job.LayoutName;
                    try
                    {
                        using (ViewTableRecord view = ed.GetCurrentView())
                        {
                            // Transform WCS bounds to DCS for PlotWindowArea and Zoom Center
                            Matrix3d matWcs2Dcs = Matrix3d.PlaneToWorld(view.ViewDirection).Inverse() * Matrix3d.Displacement(view.Target.GetAsVector().Negate());
                            matWcs2Dcs = Matrix3d.Rotation(view.ViewTwist, Vector3d.ZAxis, Point3d.Origin).Inverse() * matWcs2Dcs;

                            Point3d pMin = job.Extents.MinPoint.TransformBy(matWcs2Dcs);
                            Point3d pMax = job.Extents.MaxPoint.TransformBy(matWcs2Dcs);

                            Extents2d dcsExt = new Extents2d(
                                Math.Min(pMin.X, pMax.X), Math.Min(pMin.Y, pMax.Y),
                                Math.Max(pMin.X, pMax.X), Math.Max(pMin.Y, pMax.Y)
                            );

                            // Set PlotWindowArea with exact DCS coordinates
                            PlotSettingsValidator psv = PlotSettingsValidator.Current;
                            psv.SetPlotWindowArea(job.Ps, dcsExt);
                            psv.SetPlotType(job.Ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                            psv.SetUseStandardScale(job.Ps, true);
                            psv.SetStdScaleType(job.Ps, StdScaleType.ScaleToFit);
                            psv.SetPlotCentered(job.Ps, true);
                        }
                    }
                    catch { }

                    if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) continue;

                    using (var pe = PlotFactory.CreatePublishEngine())
                    using (var ppd = new PlotProgressDialog(false, 1, true))
                    {
                        ppd.set_PlotMsgString(PlotMessageIndex.DialogTitle, "TPL");
                        ppd.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage, "Cancel");
                        ppd.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage, "Cancel");
                        ppd.LowerPlotProgressRange = 0; ppd.UpperPlotProgressRange = 100; ppd.PlotProgressPos = 0;
                        ppd.OnBeginPlot(); ppd.IsVisible = false;
                        pe.BeginPlot(ppd, null);

                        var pi = new PlotInfo { Layout = job.LayoutId, OverrideSettings = job.Ps };
                        var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                        piv.Validate(pi);

                        pe.BeginDocument(pi, doc.Name, null, 1, true, filePath);
                        ppd.OnBeginSheet(); ppd.LowerSheetProgressRange = 0; ppd.UpperSheetProgressRange = 100; ppd.SheetProgressPos = 0;
                        pe.BeginPage(new PlotPageInfo(), pi, true, null);
                        pe.BeginGenerateGraphics(null);
                        pe.EndGenerateGraphics(null);
                        pe.EndPage(null);
                        ppd.SheetProgressPos = 100; ppd.OnEndSheet();
                        pe.EndDocument(null);
                        ppd.PlotProgressPos = 100; ppd.OnEndPlot();
                        pe.EndPlot(null);
                        generatedFiles.Add(filePath);
                    }
                    job.Ps.Dispose();
                }

                pb.Value = plotJobs.Count;
                lblProg.Text = string.Format(L10n.T("prog_progress"), plotJobs.Count, plotJobs.Count);
                progressForm.Update();

                string finalPath = generatedFiles.Count > 0 ? generatedFiles[0] : null;
                if (settings.MergePdfs && generatedFiles.Count > 1
                    && generatedFiles.All(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        lblSub.Text = L10n.T("prog_merging"); progressForm.Update();
                        string mergedPath = Path.Combine(outDir, $"{baseName}.pdf");
                        if (File.Exists(mergedPath)) File.Delete(mergedPath);
                        using (var outDoc = new PdfDocument())
                        {
                            foreach (string f in generatedFiles)
                                using (var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import))
                                    for (int p = 0; p < inDoc.PageCount; p++)
                                        outDoc.AddPage(inDoc.Pages[p]);
                            outDoc.Save(mergedPath);
                        }
                        foreach (string f in generatedFiles) { try { File.Delete(f); } catch { } }
                        ed.WriteMessage($"\nMerge OK: {mergedPath}");
                        finalPath = mergedPath;
                    }
                    catch (System.Exception ex) { ed.WriteMessage($"\nMerge error: {ex.Message}"); }
                }

                progressForm.Close(); progressForm.Dispose();
                if (settings.OpenPdf && finalPath != null && File.Exists(finalPath))
                    try { System.Diagnostics.Process.Start(finalPath); } catch { }
            }
            finally
            {
                Application.SetSystemVariable("BACKGROUNDPLOT", bgPlot);
            }
        }
    }
}
