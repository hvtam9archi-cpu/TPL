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
            return bpType switch
            {
                PlotHelper.BasePoint.BottomLeft => new Point3d(Extents.MinPoint.X, Extents.MinPoint.Y, 0),
                PlotHelper.BasePoint.BottomRight => new Point3d(Extents.MaxPoint.X, Extents.MinPoint.Y, 0),
                PlotHelper.BasePoint.TopLeft => new Point3d(Extents.MinPoint.X, Extents.MaxPoint.Y, 0),
                PlotHelper.BasePoint.TopRight => new Point3d(Extents.MaxPoint.X, Extents.MaxPoint.Y, 0),
                _ => Extents.MinPoint,
            };
        }
    }

    public static class PlotLogic
    {
        /// <summary>Ngưỡng tối thiểu cho chiều rộng/cao của plot frame (đơn vị drawing).
        /// Frame nhỏ hơn giá trị này sẽ bị bỏ qua vì gây treo PlotEngine.</summary>
        private const double MinFrameDimension = 0.001;
        public static List<PlotFrame> SelectFrames(PlotHelper.PlotSettingsData settings)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<PlotFrame> frames = new();

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
                    SelectionFilter selFilter = new(filter);
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
                // Filter INSERT + tên block cụ thể.
                // Thêm *U* để bắt dynamic block (entity lưu anonymous name *Uxx).
                string blockNameFilter = string.Join(",", settings.FrameNames);
                if (!string.IsNullOrEmpty(blockNameFilter))
                {
                    blockNameFilter += ",`*U*"; // backtick escape ký tự * đầu tiên cho AutoCAD wildcard
                    return new TypedValue[] {
                        new((int)DxfCode.Start, "INSERT"),
                        new((int)DxfCode.BlockName, blockNameFilter)
                    };
                }
                return new TypedValue[] { new((int)DxfCode.Start, "INSERT") };
            }
            else
            {
                string layerNames = string.Join(",", settings.FrameNames);
                if (string.IsNullOrEmpty(layerNames)) layerNames = "*";
                return new TypedValue[] {
                    new((int)DxfCode.Start, "LWPOLYLINE"),
                    new((int)DxfCode.LayerName, layerNames)
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

            Extents3d extents;
            if (settings.FrameType == PlotHelper.FrameType.Block)
            {
                BlockReference br = ent as BlockReference;
                if (br == null) return;
                string name = GetBlockName(br, tr);
                if (!settings.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase))) return;
                if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr);
                try { extents = CalculateBlockExtents(br, tr); }
                catch { return; } // Block không có extents hợp lệ
            }
            else
            {
                Polyline pl = ent as Polyline;
                if (pl == null) return;
                if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr);
                try { extents = pl.GeometricExtents; }
                catch { return; } // Polyline không có extents hợp lệ
            }

            // ═══ VALIDATION: bỏ qua frame không có diện tích (line, arc, polyline thẳng...) ═══
            double frameWidth = Math.Abs(extents.MaxPoint.X - extents.MinPoint.X);
            double frameHeight = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
            if (frameWidth < MinFrameDimension || frameHeight < MinFrameDimension)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TPL] Skipped frame (no area): Handle={ent.Handle}, W={frameWidth:F4}, H={frameHeight:F4}");
                return;
            }

            frames.Add(new PlotFrame { Id = id, Extents = extents, LayoutName = layoutName });
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

        /// <summary>
        /// Tính Extents của BlockReference mà bỏ qua phần DefinedWidth dư thừa của MText.
        /// Duyệt từng entity con trong Block Definition, với MText thì dùng ActualWidth/ActualHeight
        /// thay vì GeometricExtents (bị ảnh hưởng bởi DefinedWidth).
        /// </summary>
        private static Extents3d CalculateBlockExtents(BlockReference br, Transaction tr)
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord,
                OpenMode.ForRead);

            Extents3d? combinedExtents = null;
            Matrix3d blockTransform = br.BlockTransform;

            foreach (ObjectId entId in btr)
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased || !ent.Visible) continue;

                // Bỏ qua entity trên layer bị tắt/frozen
                try
                {
                    LayerTableRecord layer = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
                    if (layer.IsOff || layer.IsFrozen) continue;
                }
                catch { }

                Extents3d entExtents;
                try
                {
                    if (ent is MText mt && mt.Width > 0 && mt.ActualWidth < mt.Width)
                    {
                        // MText có DefinedWidth lớn hơn nội dung thực → tính thủ công
                        entExtents = CalculateMTextActualExtents(mt);
                    }
                    else
                    {
                        entExtents = ent.GeometricExtents;
                    }
                }
                catch { continue; } // Entity không có extents hợp lệ

                // Transform từ Block Definition Space → World Space
                entExtents.TransformBy(blockTransform);

                if (combinedExtents == null)
                    combinedExtents = entExtents;
                else
                {
                    var current = combinedExtents.Value;
                    current.AddExtents(entExtents);
                    combinedExtents = current;
                }
            }

            // Xử lý AttributeReference (nằm trực tiếp trên BlockReference, không nằm trong BTR)
            foreach (ObjectId attId in br.AttributeCollection)
            {
                try
                {
                    AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att == null || att.IsErased || !att.Visible) continue;
                    Extents3d attExt = att.GeometricExtents;
                    if (combinedExtents == null)
                        combinedExtents = attExt;
                    else
                    {
                        var current = combinedExtents.Value;
                        current.AddExtents(attExt);
                        combinedExtents = current;
                    }
                }
                catch { }
            }

            // Fallback: nếu block rỗng hoặc không tính được → dùng GeometricExtents gốc
            return combinedExtents ?? br.GeometricExtents;
        }

        /// <summary>
        /// Tính bounding box thực tế của MText dựa trên ActualWidth/ActualHeight,
        /// bỏ qua DefinedWidth (Width) bị kéo dài.
        /// </summary>
        private static Extents3d CalculateMTextActualExtents(MText mt)
        {
            double w = mt.ActualWidth;
            double h = mt.ActualHeight;
            Point3d loc = mt.Location;

            // Tính offset dựa trên Attachment point
            double offsetX = 0, offsetY = 0;
            switch (mt.Attachment)
            {
                case AttachmentPoint.TopLeft:
                case AttachmentPoint.MiddleLeft:
                case AttachmentPoint.BottomLeft:
                    offsetX = 0; break;
                case AttachmentPoint.TopCenter:
                case AttachmentPoint.MiddleCenter:
                case AttachmentPoint.BottomCenter:
                    offsetX = -w / 2; break;
                case AttachmentPoint.TopRight:
                case AttachmentPoint.MiddleRight:
                case AttachmentPoint.BottomRight:
                    offsetX = -w; break;
            }
            switch (mt.Attachment)
            {
                case AttachmentPoint.TopLeft:
                case AttachmentPoint.TopCenter:
                case AttachmentPoint.TopRight:
                    offsetY = -h; break;
                case AttachmentPoint.MiddleLeft:
                case AttachmentPoint.MiddleCenter:
                case AttachmentPoint.MiddleRight:
                    offsetY = -h / 2; break;
                case AttachmentPoint.BottomLeft:
                case AttachmentPoint.BottomCenter:
                case AttachmentPoint.BottomRight:
                    offsetY = 0; break;
            }

            Point3d minPt = new Point3d(loc.X + offsetX, loc.Y + offsetY, loc.Z);
            Point3d maxPt = new Point3d(loc.X + offsetX + w, loc.Y + offsetY + h, loc.Z);

            // Xử lý MText có Rotation
            if (Math.Abs(mt.Rotation) > 1e-6)
            {
                Matrix3d rotMat = Matrix3d.Rotation(mt.Rotation, mt.Normal, loc);
                Point3d p1 = minPt.TransformBy(rotMat);
                Point3d p2 = new Point3d(maxPt.X, minPt.Y, loc.Z).TransformBy(rotMat);
                Point3d p3 = maxPt.TransformBy(rotMat);
                Point3d p4 = new Point3d(minPt.X, maxPt.Y, loc.Z).TransformBy(rotMat);

                minPt = new Point3d(
                    Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X)),
                    Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y)),
                    loc.Z);
                maxPt = new Point3d(
                    Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X)),
                    Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y)),
                    loc.Z);
            }

            return new Extents3d(minPt, maxPt);
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

            // ═══ PRE-FILTER: loại bỏ frame không có diện tích trước khi plot ═══
            int originalCount = frames.Count;
            frames.RemoveAll(f =>
            {
                double w = Math.Abs(f.Extents.MaxPoint.X - f.Extents.MinPoint.X);
                double h = Math.Abs(f.Extents.MaxPoint.Y - f.Extents.MinPoint.Y);
                return w < MinFrameDimension || h < MinFrameDimension;
            });
            int skippedCount = originalCount - frames.Count;
            if (skippedCount > 0)
            {
                ed.WriteMessage($"\n[TPL] WARNING: Skipped {skippedCount} frame(s) with zero area (line/arc/degenerate).");
            }
            if (frames.Count == 0) { ed.WriteMessage("\n[TPL] No valid plot frames remaining after filtering."); return; }

            bool isFilePrinter = PlotHelper.IsFilePrinter(settings.DeviceName);

            string baseName = settings.BaseFileName;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Drawing1";
            string ext = ".plt";
            string outDir = "";

            // Chỉ chuẩn bị file output nếu là máy in file
            if (isFilePrinter)
            {
                ext = settings.DeviceName.ToLower().Contains("pdf") ? ".pdf" : ".plt";
                outDir = settings.OutputPath;
                if (!outDir.EndsWith("\\")) outDir += "\\";
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            }

            // Phase 1: pre-collect all layout/plot data inside ONE transaction
            // PlotEngine must NEVER run while a transaction is active.
            var plotJobs = new List<(string LayoutName, ObjectId LayoutId, bool ModelType, PlotSettings Ps, Extents3d Extents)>();
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                foreach (var frame in frames)
                {
                    ObjectId layId = LayoutManager.Current.GetLayoutId(frame.LayoutName);
                    Layout lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                    PlotSettings ps = new(lay.ModelType);
                    ps.CopyFrom(lay);
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;

                    // 1. Set Device and Paper Size FIRST
                    try { psv.SetPlotConfigurationName(ps, settings.DeviceName, settings.PaperSize.Replace(" ", "_")); } catch { }

                    // 2. Set PlotWindowArea (dummy or real) BEFORE PlotType
                    Extents2d plotExt;
                    if (lay.ModelType)
                    {
                        using ViewTableRecord vtr = ed.GetCurrentView();
                        Matrix3d matWCS2DCS = Matrix3d.WorldToPlane(vtr.ViewDirection) *
                                              Matrix3d.Displacement(Point3d.Origin - vtr.Target) *
                                              Matrix3d.Rotation(vtr.ViewTwist, vtr.ViewDirection, vtr.Target);

                        Point3d p1 = new Point3d(frame.Extents.MinPoint.X, frame.Extents.MinPoint.Y, 0).TransformBy(matWCS2DCS);
                        Point3d p2 = new Point3d(frame.Extents.MaxPoint.X, frame.Extents.MinPoint.Y, 0).TransformBy(matWCS2DCS);
                        Point3d p3 = new Point3d(frame.Extents.MaxPoint.X, frame.Extents.MaxPoint.Y, 0).TransformBy(matWCS2DCS);
                        Point3d p4 = new Point3d(frame.Extents.MinPoint.X, frame.Extents.MaxPoint.Y, 0).TransformBy(matWCS2DCS);

                        double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
                        double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
                        double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
                        double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

                        plotExt = new Extents2d(minX, minY, maxX, maxY);
                    }
                    else
                    {
                        plotExt = new Extents2d(frame.Extents.MinPoint.X, frame.Extents.MinPoint.Y, frame.Extents.MaxPoint.X, frame.Extents.MaxPoint.Y);
                    }
                    psv.SetPlotWindowArea(ps, plotExt);

                    // 3. Set PlotType
                    psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);

                    // 4. Set PlotWindowArea AGAIN to ensure it isn't reset by PlotType
                    psv.SetPlotWindowArea(ps, plotExt);

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
                    bool frameIsLandscape = lenX > lenY;

                    // Nhận biết hướng của giấy được thiết lập trong PlotSettings (sau khi chọn máy in & khổ giấy)
                    bool paperIsLandscape = true; // mặc định/fallback
                    if (ps.PlotPaperSize.X > 0 && ps.PlotPaperSize.Y > 0)
                    {
                        paperIsLandscape = ps.PlotPaperSize.X > ps.PlotPaperSize.Y;
                    }

                    // Xác định hướng mong muốn dựa trên cấu hình UI (Auto, Portrait, Landscape)
                    bool targetIsLandscape = frameIsLandscape;
                    if (settings.Orientation == PlotHelper.PlotOrientation.Portrait)
                    {
                        targetIsLandscape = false;
                    }
                    else if (settings.Orientation == PlotHelper.PlotOrientation.Landscape)
                    {
                        targetIsLandscape = true;
                    }

                    // Nếu hướng mong muốn khác hướng của giấy thiết lập, xoay 90 độ
                    PlotRotation rotation = (targetIsLandscape == paperIsLandscape) ? PlotRotation.Degrees000 : PlotRotation.Degrees090;
                    psv.SetPlotRotation(ps, rotation);

                    plotJobs.Add((frame.LayoutName, layId, lay.ModelType, ps, frame.Extents));
                }
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPlot data error: {ex.Message}");
                return;
            }

            // Phase 2: PlotEngine + Regen with NO active transaction
            short bgPlot = (short)Application.GetSystemVariable("BACKGROUNDPLOT");
            Application.SetSystemVariable("BACKGROUNDPLOT", 0);

            var progressWin = new ProgressWindow(L10n.T("prog_title"), plotJobs.Count);
            progressWin.Show();

            var generatedFiles = new List<string>();
            int errorCount = 0;
            try
            {
                int fileCounter = 1;
                for (int i = 0; i < plotJobs.Count; i++)
                {
                    var (LayoutName, LayoutId, ModelType, Ps, Extents) = plotJobs[i];
                    try
                    {
                        // ═══ SAFETY CHECK: validate extents trước khi gửi vào PlotEngine ═══
                        double extW = Math.Abs(Extents.MaxPoint.X - Extents.MinPoint.X);
                        double extH = Math.Abs(Extents.MaxPoint.Y - Extents.MinPoint.Y);
                        if (extW < MinFrameDimension || extH < MinFrameDimension)
                        {
                            ed.WriteMessage($"\n[TPL] Skipped page {i + 1}: Frame has no area (W={extW:F4}, H={extH:F4}).");
                            errorCount++;
                            continue;
                        }

                        string filePath = "";
                        string fileName = "";

                        // Chỉ tạo file path nếu là máy in file
                        if (isFilePrinter)
                        {
                            do
                            {
                                fileName = $"{baseName}_{fileCounter:D2}{ext}";
                                filePath = Path.Combine(outDir, fileName);
                                fileCounter++;
                            } while (File.Exists(filePath));
                        }
                        else
                        {
                            fileName = $"Page {i + 1}/{plotJobs.Count}";
                        }

                        string subLabel = isFilePrinter
                            ? string.Format(L10n.T("prog_file"), fileName)
                            : $"Printing: {fileName} → {settings.DeviceName}";
                        progressWin.UpdateProgress(i, string.Format(L10n.T("prog_progress"), i + 1, plotJobs.Count), subLabel);

                        // Layout switch
                        LayoutManager.Current.CurrentLayout = LayoutName;
                        ed.UpdateScreen();

                        if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                        {
                            ed.WriteMessage($"\n[TPL] Skipped page {i + 1}: PlotEngine busy.");
                            errorCount++;
                            continue;
                        }

                        {
						    // ═══ Unified Plot Engine ═══
						    using var pe = PlotFactory.CreatePublishEngine();
						    using var ppd = new PlotProgressDialog(false, 1, true);
						    ppd.set_PlotMsgString(PlotMessageIndex.DialogTitle, "TPL");
						    ppd.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage, "Cancel");
						    ppd.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage, "Cancel");
						    ppd.LowerPlotProgressRange = 0; ppd.UpperPlotProgressRange = 100; ppd.PlotProgressPos = 0;
						    ppd.OnBeginPlot(); ppd.IsVisible = false;
						    pe.BeginPlot(ppd, null);

						    var pi = new PlotInfo { Layout = LayoutId, OverrideSettings = Ps };
						    var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
						    piv.Validate(pi);

						    pe.BeginDocument(pi, doc.Name, null, 1, isFilePrinter, isFilePrinter ? filePath : "");
						    ppd.OnBeginSheet(); ppd.LowerSheetProgressRange = 0; ppd.UpperSheetProgressRange = 100; ppd.SheetProgressPos = 0;
						    pe.BeginPage(new PlotPageInfo(), pi, true, null);
						    pe.BeginGenerateGraphics(null);
						    pe.EndGenerateGraphics(null);
						    pe.EndPage(null);
						    ppd.SheetProgressPos = 100; ppd.OnEndSheet();
						    pe.EndDocument(null);
						    ppd.PlotProgressPos = 100; ppd.OnEndPlot();
						    pe.EndPlot(null);
						    if (isFilePrinter) generatedFiles.Add(filePath);
						}
                    }
                    catch (System.Exception plotEx)
                    {
                        // ═══ CRASH-PROOF: bắt lỗi từng trang, không crash toàn batch ═══
                        errorCount++;
                        ed.WriteMessage($"\n[TPL] ERROR page {i + 1}: {plotEx.GetType().Name}: {plotEx.Message}");
                    }
                    finally
                    {
                        Ps.Dispose();
                    }
                }

                // Thông báo tổng kết lỗi
                if (errorCount > 0)
                {
                    ed.WriteMessage($"\n[TPL] Completed with {errorCount} error(s) out of {plotJobs.Count} page(s).");
                }

                progressWin.UpdateProgress(plotJobs.Count, string.Format(L10n.T("prog_progress"), plotJobs.Count, plotJobs.Count), "");

                // ═══ POST-PROCESSING (chỉ áp dụng cho máy in file) ═══
                if (isFilePrinter)
                {
                    // Phase 2: Post-processing Orientation
                    if (settings.Orientation != PlotHelper.PlotOrientation.Auto && generatedFiles.Count > 0)
                    {
                        progressWin.SetSubTitle("Applying Orientation...");
                        foreach (string pdfFile in generatedFiles)
                        {
                            if (!pdfFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                using var docPdf = PdfReader.Open(pdfFile, PdfDocumentOpenMode.Modify);
                                bool modified = false;
                                foreach (var page in docPdf.Pages)
                                {
                                    // Use raw MediaBox to avoid any PdfSharp version differences in page.Width
                                    double rawWidth = page.MediaBox.Width;
                                    double rawHeight = page.MediaBox.Height;

                                    // Calculate visual dimensions based on current Rotate
                                    int currentRotate = page.Rotate;
                                    bool isRotated = (Math.Abs(currentRotate) / 90) % 2 != 0;

                                    double visualWidth = isRotated ? rawHeight : rawWidth;
                                    double visualHeight = isRotated ? rawWidth : rawHeight;

                                    if (settings.Orientation == PlotHelper.PlotOrientation.Portrait && visualWidth > visualHeight)
                                    {
                                        page.Rotate = (currentRotate + 90) % 360; // 90 deg CW
                                        modified = true;
                                    }
                                    else if (settings.Orientation == PlotHelper.PlotOrientation.Landscape && visualHeight > visualWidth)
                                    {
                                        page.Rotate = (currentRotate + 270) % 360; // 90 deg CCW
                                        modified = true;
                                    }
                                }
                                if (modified) docPdf.Save(pdfFile);
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\nOrientation error on {pdfFile}: {ex.Message}");
                            }
                        }
                    }

                    string finalPath = generatedFiles.Count > 0 ? generatedFiles[0] : null;
                    if (settings.MergePdfs && generatedFiles.Count > 1
                        && generatedFiles.All(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            progressWin.SetSubTitle(L10n.T("prog_merging"));
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
                    else if (settings.ConvertToImage && generatedFiles.All(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            progressWin.SetSubTitle("Converting to Image...");
                            List<string> imageFiles = new();
                            for (int i = 0; i < generatedFiles.Count; i++)
                            {
                                string pdfFile = generatedFiles[i];
                                string imgExt = settings.ImageFormat.ToLower();
                                string imgPath = Path.ChangeExtension(pdfFile, imgExt);

                                using (var docPdf = PdfiumViewer.PdfDocument.Load(pdfFile))
                                {
                                    // Auto-calculate size from DPI
                                    var size = docPdf.PageSizes[0];
                                    int width = (int)(size.Width * settings.ImageDpi / 72.0);
                                    int height = (int)(size.Height * settings.ImageDpi / 72.0);

                                    using var image = docPdf.Render(0, width, height, settings.ImageDpi, settings.ImageDpi, PdfiumViewer.PdfRenderFlags.Annotations);
                                    if (settings.ImageFormat == "JPG")
                                        image.Save(imgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                                    else
                                        image.Save(imgPath, System.Drawing.Imaging.ImageFormat.Png);
                                }
                                imageFiles.Add(imgPath);
                                try { File.Delete(pdfFile); } catch { }
                            }

                            ed.WriteMessage($"\nConvert Image OK: {imageFiles.Count} files.");
                            if (imageFiles.Count > 0) finalPath = imageFiles[0];
                        }
                        catch (System.Exception ex) { ed.WriteMessage($"\nConvert Image error: {ex.Message}"); }
                    }
                    else if (settings.PdfEditor && generatedFiles.Count > 0 && generatedFiles.All(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var editor = PdfEditorWindow.Instance;
                            editor.SetDefaultFileName(baseName);
                            editor.AddPdfFiles(generatedFiles);

                            Commands.MainFormInstance?.Hide();

                            if (!editor.IsVisible)
                                editor.Show();
                            else
                                editor.Activate();
                        }
                        catch (System.Exception ex) { ed.WriteMessage($"\nPDF Editor error: {ex.Message}"); }
                    }

                    progressWin.Close();
                    if (settings.OpenPdf && finalPath != null && File.Exists(finalPath))
                        try { System.Diagnostics.Process.Start(finalPath); } catch { }
                }
                else
                {
                    // Máy in vật lý: chỉ đóng progress, không post-process
                    progressWin.Close();
                    ed.WriteMessage($"\nTPL: Sent {plotJobs.Count} page(s) to printer [{settings.DeviceName}].");
                }
            }
            finally
            {
                try { progressWin.Close(); } catch { }
                Application.SetSystemVariable("BACKGROUNDPLOT", bgPlot);
            }
        }
    }
}
