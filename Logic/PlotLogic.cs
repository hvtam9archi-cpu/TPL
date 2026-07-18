using Prima.VinaCAD.ApplicationServices;
using Teigha.DatabaseServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;

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
		private static readonly object PdfExportModuleLock = new();
		private static bool _pdfExportModuleLoaded;
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
                    blockNameFilter += ",`*U*"; // backtick escape ký tự * đầu tiên cho VinaCAD wildcard
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
            {
                AssignFrameOrder(frames);
                return;
            }

            if (settings.GroupOrder == PlotHelper.SortOrder.MarkedOrder)
            {
                AssignFrameOrder(frames);
                return;
            }

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

            AssignFrameOrder(frames);
        }

        private static void AssignFrameOrder(List<PlotFrame> frames)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i].OrderIndex = i + 1;
                frames[i].MarkerText = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// PlotSettings expects a window in DCS for Model Space. Paper Space already
        /// uses layout coordinates, so its extents can be passed through unchanged.
        /// </summary>
        private static Extents2d GetPlotWindowArea(Editor ed, Extents3d worldExtents, bool modelType)
        {
            if (!modelType)
            {
                return new Extents2d(
                    worldExtents.MinPoint.X, worldExtents.MinPoint.Y,
                    worldExtents.MaxPoint.X, worldExtents.MaxPoint.Y);
            }

            using ViewTableRecord view = ed.GetCurrentView();
            Matrix3d worldToDisplay = Matrix3d.WorldToPlane(view.ViewDirection)
                * Matrix3d.Displacement(Point3d.Origin - view.Target)
                * Matrix3d.Rotation(view.ViewTwist, view.ViewDirection, view.Target);

            Point3d[] corners =
            {
                new Point3d(worldExtents.MinPoint.X, worldExtents.MinPoint.Y, 0).TransformBy(worldToDisplay),
                new Point3d(worldExtents.MaxPoint.X, worldExtents.MinPoint.Y, 0).TransformBy(worldToDisplay),
                new Point3d(worldExtents.MaxPoint.X, worldExtents.MaxPoint.Y, 0).TransformBy(worldToDisplay),
                new Point3d(worldExtents.MinPoint.X, worldExtents.MaxPoint.Y, 0).TransformBy(worldToDisplay)
            };

            return new Extents2d(
                corners.Min(point => point.X), corners.Min(point => point.Y),
                corners.Max(point => point.X), corners.Max(point => point.Y));
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
            string ext = ".pdf";
            string outDir = "";

            // Chỉ chuẩn bị file output nếu là máy in file
            if (isFilePrinter)
            {
                ext = settings.DeviceName.ToLower().Contains("pdf") ? ".pdf" : ".plt";
                outDir = settings.OutputPath;
                if (!outDir.EndsWith("\\")) outDir += "\\";
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            }

            var generatedFiles = new List<string>();
            int errorCount = 0;
            int fileCounter = 1;

            var progressWin = new ProgressWindow(L10n.T("prog_title"), frames.Count);
            try
            {
                var acWin = Application.MainWindow;
                if (acWin != null)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(progressWin)
                    {
                        Owner = acWin.Handle
                    };
                }
            }
            catch { }
            progressWin.Show();

            try
            {
                if (isFilePrinter)
                {
                    // ═══ PDF Export via Teigha.Export_Import API ═══
                    PlotAllWithExportPdf(frames, settings, db, ed, baseName, ext, outDir,
                        generatedFiles, ref errorCount, ref fileCounter, progressWin);
                }
                else
                {
                    // ═══ Máy in vật lý: fallback qua SendStringToExecute ═══
                    PlotAllWithSendString(frames, settings, doc, ed, ref errorCount, progressWin);
                }

                // Thông báo tổng kết lỗi
                if (errorCount > 0)
                {
                    ed.WriteMessage($"\n[TPL] Completed with {errorCount} error(s) out of {frames.Count} page(s).");
                }

                progressWin.UpdateProgress(frames.Count, string.Format(L10n.T("prog_progress"), frames.Count, frames.Count), "");

                // ═══ POST-PROCESSING (chỉ áp dụng cho máy in file) ═══
                if (isFilePrinter)
                {
                    PostProcessFiles(frames, settings, ed, generatedFiles, baseName, outDir, progressWin);
                }
                else
                {
                    // Máy in vật lý: chỉ đóng progress, không post-process
                    progressWin.Close();
                    ed.WriteMessage($"\nTPL: Sent {frames.Count} page(s) to printer [{settings.DeviceName}].");
                }
            }
            finally
            {
                try { progressWin.Close(); } catch { }
            }
        }

        /// <summary>
        /// Xuất PDF cho từng frame sử dụng Teigha.Export_Import.ExportPDF API.
        /// Mỗi frame được export riêng bằng cách cấu hình PlotSettings trên Layout
        /// với PlotType = Window, sau đó gọi ExportPDF.
        /// </summary>
        private static void PlotAllWithExportPdf(
            List<PlotFrame> frames,
            PlotHelper.PlotSettingsData settings,
            Database db,
            Editor ed,
            string baseName,
            string ext,
            string outDir,
            List<string> generatedFiles,
            ref int errorCount,
            ref int fileCounter,
            ProgressWindow progressWin)
        {
			EnsurePdfExportModuleLoaded();

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
				string stage = "validating frame";
                try
                {
                    // ═══ SAFETY CHECK: validate extents ═══
                    double extW = Math.Abs(frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X);
                    double extH = Math.Abs(frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y);
                    if (extW < MinFrameDimension || extH < MinFrameDimension)
                    {
                        ed.WriteMessage($"\n[TPL] Skipped page {i + 1}: Frame has no area (W={extW:F4}, H={extH:F4}).");
                        errorCount++;
                        continue;
                    }

                    string fileName;
                    string filePath;
                    do
                    {
                        fileName = $"{baseName}_{fileCounter:D2}{ext}";
                        filePath = Path.Combine(outDir, fileName);
                        fileCounter++;
                    } while (File.Exists(filePath));

                    string subLabel = string.Format(L10n.T("prog_file"), fileName);
                    progressWin.UpdateProgress(i, string.Format(L10n.T("prog_progress"), i + 1, frames.Count), subLabel);

					stage = "switching layout";
                    LayoutManager.Current.CurrentLayout = frame.LayoutName;
					ed.UpdateScreen();

					double paperWidth = 0;
					double paperHeight = 0;
					Extents2d windowArea = default;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        // Lấy Layout hiện tại
                        var layoutMgr = LayoutManager.Current;
                        var btRecord = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                        var layout = (Layout)tr.GetObject(btRecord.LayoutId, OpenMode.ForWrite);

                        // Cấu hình PlotSettings trên Layout cho vùng Window
                        var psv = PlotSettingsValidator.Current;
						stage = "setting plot device";
                        psv.SetPlotConfigurationName(layout, settings.DeviceName, null);
                        psv.RefreshLists(layout);

						// Resolve the exact canonical media name returned by this device. The UI
						// displays underscores as spaces, so blindly reversing the text is unsafe.
						stage = "setting canonical media";
						string canonicalMedia = psv.GetCanonicalMediaNameList(layout)
							.Cast<string>()
							.FirstOrDefault(name => string.Equals(
								name.Replace("_", " "), settings.PaperSize,
								StringComparison.OrdinalIgnoreCase));
						if (string.IsNullOrEmpty(canonicalMedia))
							throw new InvalidOperationException($"Paper size is unavailable for device: {settings.PaperSize}");
						psv.SetCanonicalMediaName(layout, canonicalMedia);

						// Model Space requires DCS coordinates; Paper Space uses layout coordinates.
						windowArea = GetPlotWindowArea(ed, frame.Extents, layout.ModelType);
						stage = "setting plot window";
						// VinaCAD/Teigha requires a valid window before switching to Window mode.
						psv.SetPlotWindowArea(layout, windowArea);
						psv.SetPlotType(layout, Teigha.DatabaseServices.PlotType.Window);
                        psv.SetPlotWindowArea(layout, windowArea);

                        // Scale to Fit + Center
						stage = "setting plot scale";
                        psv.SetUseStandardScale(layout, true);
                        psv.SetStdScaleType(layout, StdScaleType.ScaleToFit);
                        psv.SetPlotCentered(layout, true);

                        // Paper units = Millimeters
                        psv.SetPlotPaperUnits(layout, PlotPaperUnit.Millimeters);

                        // Orientation
                        if (settings.Orientation == PlotHelper.PlotOrientation.Portrait)
                            psv.SetPlotRotation(layout, PlotRotation.Degrees000);
                        else if (settings.Orientation == PlotHelper.PlotOrientation.Landscape)
                            psv.SetPlotRotation(layout, PlotRotation.Degrees090);

                        // Plot styles
                        if (!string.IsNullOrEmpty(settings.PlotStyle))
                        {
                            try { psv.SetCurrentStyleSheet(layout, settings.PlotStyle); }
                            catch { }
                        }

                        layout.PrintLineweights = true;
                        layout.PlotPlotStyles = true;

						paperWidth = layout.PlotPaperSize.X;
						paperHeight = layout.PlotPaperSize.Y;
						if (paperWidth <= 0 || paperHeight <= 0)
							throw new InvalidOperationException($"Invalid plot paper dimensions: {paperWidth} x {paperHeight}");

						stage = "committing plot settings";
                        tr.Commit();
                    }

					stage = "constructing PDF export parameters";
                    using (var pdfParams = new Teigha.Export_Import.mPDFExportParams())
                    {
						stage = "assigning PDF database";
                        pdfParams.Database = db;

                        // Chỉ export layout hiện tại
                        var layouts = new System.Collections.Specialized.StringCollection();
                        layouts.Add(frame.LayoutName);
						stage = "assigning PDF layouts";
                        pdfParams.Layouts = layouts;

						// VinaCAD's PDF exporter requires one PageParams entry for every
						// layout. Leaving this collection empty raises error 65543:
						// "Number of Layouts are not equal to number of pages".
						stage = "creating PDF page parameters";
						var pageParams = new Teigha.GraphicsSystem.PageParams();
						pageParams.setParams(paperWidth, paperHeight);
						var pages = new Teigha.GraphicsSystem.PageParamsCollection();
						pages.Add(pageParams);
						pdfParams.PageParams = pages;

						stage = "assigning PDF metadata";
                        pdfParams.Title = baseName;
                        pdfParams.Author = "TPL Plugin";

						// Default contains ZoomToExtentsMode, which overrides PlotType.Window
						// and PlotWindowArea. Remove only that flag so ExportPDF uses the
						// PlotSettings stored on this layout.
						stage = "assigning PDF flags";
						var exportFlags = Teigha.Export_Import.PDFExportFlags.Default
							| Teigha.Export_Import.PDFExportFlags.EnableLayers;
						exportFlags &= ~Teigha.Export_Import.PDFExportFlags.ZoomToExtentsMode;
						pdfParams.Flags = exportFlags;

						stage = "assigning PDF compression";
                        pdfParams.FlateCompression = true;
                        pdfParams.EmbeddedOptimizedTTF = true;

                        // Output stream = file
						stage = "opening PDF output stream";
						using (var stream = new Teigha.Runtime.FileStreamBuf(
							filePath,
							openForRead: false,
							Teigha.Runtime.FileShareMode.DenyReadWrite,
							Teigha.Runtime.FileCreationDisposition.CreateAlways))
                        {
							stage = "assigning PDF output stream";
                            pdfParams.OutputStream = stream;

							stage = "exporting PDF";
                            Teigha.Export_Import.Export_Import.ExportPDF(pdfParams);
                        }
                    }

                    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                    {
                        generatedFiles.Add(filePath);
                        ed.WriteMessage($"\n[TPL] Page {i + 1}/{frames.Count}: {fileName}");
                    }
                    else
                    {
                        ed.WriteMessage($"\n[TPL] WARNING: Page {i + 1} exported but file is empty or missing: {fileName}");
                        errorCount++;
                    }
                }
                catch (System.Exception plotEx)
                {
                    errorCount++;
					ed.WriteMessage(
						$"\n[TPL] ERROR page {i + 1} at {stage}: {plotEx.GetType().Name}: {plotEx.Message}" +
						$" [Device={settings.DeviceName}; Paper={settings.PaperSize}]");
                }
            }
        }

		/// <summary>
		/// The managed PDF wrapper does not initialize its native implementation.
		/// Load the matching Teigha TX module from the host installation once.
		/// </summary>
		private static void EnsurePdfExportModuleLoaded()
		{
			if (_pdfExportModuleLoaded) return;

			lock (PdfExportModuleLock)
			{
				if (_pdfExportModuleLoaded) return;

				string teighaFolder = Path.GetDirectoryName(typeof(Teigha.Runtime.SystemObjects).Assembly.Location);
				string modulePath = Directory
					.GetFiles(teighaFolder, "TD_PdfExport_*.tx", SearchOption.TopDirectoryOnly)
					.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
					.FirstOrDefault();

				if (string.IsNullOrEmpty(modulePath))
					throw new FileNotFoundException("Teigha PDF export module was not found.", teighaFolder);

				var dynamicLinker = Teigha.Runtime.SystemObjects.DynamicLinker;
				string moduleName = Path.GetFileName(modulePath);
				if (!dynamicLinker.IsModuleLoaded(moduleName) && !dynamicLinker.IsModuleLoaded(modulePath))
				{
					var module = dynamicLinker.LoadModule(modulePath, false, true);
					if (module == null)
						throw new InvalidOperationException($"VinaCAD could not load PDF export module: {moduleName}");
				}

				_pdfExportModuleLoaded = true;
			}
		}

        /// <summary>
        /// Fallback cho máy in vật lý: gửi lệnh PLOT qua SendStringToExecute.
        /// </summary>
        private static void PlotAllWithSendString(
            List<PlotFrame> frames,
            PlotHelper.PlotSettingsData settings,
            Document doc,
            Editor ed,
            ref int errorCount,
            ProgressWindow progressWin)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                try
                {
                    string subLabel = $"Printing: Page {i + 1}/{frames.Count} → {settings.DeviceName}";
                    progressWin.UpdateProgress(i, string.Format(L10n.T("prog_progress"), i + 1, frames.Count), subLabel);

                    LayoutManager.Current.CurrentLayout = frame.LayoutName;
                    ed.UpdateScreen();

                    // Gửi lệnh PLOT qua SendStringToExecute
                    string cmdString = $"(command \"_-PLOT\" \"Yes\" \"{frame.LayoutName}\" \"{settings.DeviceName}\" " +
                        $"\"{settings.PaperSize.Replace(" ", "_")}\" \"Millimeters\" " +
                        $"\"{(settings.Orientation == PlotHelper.PlotOrientation.Portrait ? "Portrait" : "Landscape")}\" " +
                        $"\"No\" \"Window\" " +
                        $"\"{frame.Extents.MinPoint.X},{frame.Extents.MinPoint.Y}\" " +
                        $"\"{frame.Extents.MaxPoint.X},{frame.Extents.MaxPoint.Y}\" " +
                        $"\"Fit\" \"Center\" \"Yes\" \"{settings.PlotStyle}\" " +
                        $"\"Yes\" \"No\" \"Yes\" \"No\" \"No\" \"No\" \"Yes\")\n";

                    doc.SendStringToExecute(cmdString, true, false, true);
                }
                catch (System.Exception plotEx)
                {
                    errorCount++;
                    ed.WriteMessage($"\n[TPL] ERROR page {i + 1}: {plotEx.GetType().Name}: {plotEx.Message}");
                }
            }
        }

        /// <summary>
        /// Post-processing: Orientation, Merge PDFs, Convert to Image, PDF Editor.
        /// </summary>
        private static void PostProcessFiles(
            List<PlotFrame> frames,
            PlotHelper.PlotSettingsData settings,
            Editor ed,
            List<string> generatedFiles,
            string baseName,
            string outDir,
            ProgressWindow progressWin)
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
                    List<string> imageFiles = ConvertPdfFilesToImages(generatedFiles, settings);

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

                    try
                    {
                        var acWin = Application.MainWindow;
                        if (acWin != null)
                        {
                            var helper = new System.Windows.Interop.WindowInteropHelper(editor)
                            {
                                Owner = acWin.Handle
                            };
                        }
                    }
                    catch { }

                    if (!editor.IsVisible)
                        Application.ShowModelessWindow(editor);
                    else
                        editor.Activate();
                }
                catch (System.Exception ex) { ed.WriteMessage($"\nPDF Editor error: {ex.Message}"); }
            }

            progressWin.Close();
            if (settings.OpenPdf && finalPath != null && File.Exists(finalPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = finalPath,
                        UseShellExecute = true
                    });
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nOpen PDF error: {ex.Message}");
                }
            }
        }

		// Keep System.Drawing types outside PostProcessFiles so PDF-only workflows do
		// not trigger System.Drawing.Common loading during JIT compilation.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static List<string> ConvertPdfFilesToImages(
			List<string> generatedFiles,
			PlotHelper.PlotSettingsData settings)
		{
			List<string> imageFiles = new();
			foreach (string pdfFile in generatedFiles)
			{
				string imgExt = settings.ImageFormat.ToLowerInvariant();
				string imgPath = Path.ChangeExtension(pdfFile, imgExt);

				using (var docPdf = PdfiumViewer.PdfDocument.Load(pdfFile))
				{
					var size = docPdf.PageSizes[0];
					int width = (int)(size.Width * settings.ImageDpi / 72.0);
					int height = (int)(size.Height * settings.ImageDpi / 72.0);

					using var image = docPdf.Render(0, width, height, settings.ImageDpi, settings.ImageDpi,
						PdfiumViewer.PdfRenderFlags.Annotations);
					image.Save(imgPath, settings.ImageFormat == "JPG"
						? System.Drawing.Imaging.ImageFormat.Jpeg
						: System.Drawing.Imaging.ImageFormat.Png);
				}

				imageFiles.Add(imgPath);
				try { File.Delete(pdfFile); } catch { }
			}

			return imageFiles;
		}
    }
}
