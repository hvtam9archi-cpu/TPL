using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TPL
{
	public class PlotFrame
	{
		public ObjectId Id { get; set; }
		public Extents3d Extents { get; set; }
		public string LayoutName { get; set; }

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

		private sealed class FrameScanCache
		{
			public FrameScanCache(IEnumerable<string> frameNames)
			{
				FrameNames = new HashSet<string>(frameNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			}

			public HashSet<string> FrameNames { get; }
			public Dictionary<ObjectId, bool> VisibleLayers { get; } = new();
			public Dictionary<ObjectId, string> LayoutNames { get; } = new();
			public Dictionary<ObjectId, string> BlockNames { get; } = new();
			public Dictionary<ObjectId, List<Extents3d>> BlockDefinitionExtents { get; } = new();
		}

		private sealed class SortFrame
		{
			public PlotFrame Frame { get; init; }
			public Point3d BasePoint { get; init; }
			public int OriginalIndex { get; init; }
			public int GroupIndex { get; set; }
		}

		public static List<PlotFrame> SelectFrames(PlotHelper.PlotSettingsData settings)
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;
			List<PlotFrame> frames = new();
			var cache = new FrameScanCache(settings.FrameNames);

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				if (settings.SelectionMode == PlotHelper.SelectionMode.Manual)
				{
					if (settings.ManualSelectionIds != null)
					{
						foreach (ObjectId id in settings.ManualSelectionIds)
						{
							if (id.IsErased || id.IsNull) continue;
							AddFrame(frames, tr, id, settings, cache);
						}
					}
				}
				else
				{
					string currentLayout = LayoutManager.Current.CurrentLayout;
					TypedValue[] filter = GetFilter(settings, currentLayout);
					SelectionFilter selFilter = new(filter);
					PromptSelectionResult psr = ed.SelectAll(selFilter);

					if (psr.Status == PromptStatus.OK)
					{
						foreach (ObjectId id in psr.Value.GetObjectIds())
						{
							if (id.IsErased || id.IsNull) continue;
							Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead, false, true);
							if (ent == null || ent.IsErased) continue;
							string entLayout = GetLayoutName(ent.OwnerId, tr, cache);

							if (!string.Equals(entLayout, currentLayout, StringComparison.OrdinalIgnoreCase))
								continue;

							AddFrame(frames, tr, id, settings, cache, entLayout, ent);
						}
					}
				}
				tr.Commit();
			}

			return frames;
		}

		public static TypedValue[] GetFilter(PlotHelper.PlotSettingsData settings, string layoutName = null)
		{
			if (settings.FrameType == PlotHelper.FrameType.Block)
			{
				// Filter INSERT + tên block cụ thể.
				// Thêm *U* để bắt dynamic block (entity lưu anonymous name *Uxx).
				string blockNameFilter = string.Join(",", settings.FrameNames);
				if (!string.IsNullOrEmpty(blockNameFilter))
				{
					blockNameFilter += ",`*U*"; // backtick escape ký tự * đầu tiên cho AutoCAD wildcard
					return AddLayoutFilter(new TypedValue[] {
						new((int)DxfCode.Start, "INSERT"),
						new((int)DxfCode.BlockName, blockNameFilter)
					}, layoutName);
				}
				return AddLayoutFilter(new TypedValue[] { new((int)DxfCode.Start, "INSERT") }, layoutName);
			}
			else
			{
				string layerNames = string.Join(",", settings.FrameNames);
				if (string.IsNullOrEmpty(layerNames)) layerNames = "*";
				return AddLayoutFilter(new TypedValue[] {
					new((int)DxfCode.Start, "LWPOLYLINE"),
					new((int)DxfCode.LayerName, layerNames)
				}, layoutName);
			}
		}

		private static TypedValue[] AddLayoutFilter(TypedValue[] entityFilter, string layoutName)
		{
			if (string.IsNullOrWhiteSpace(layoutName)) return entityFilter;

			var result = new TypedValue[entityFilter.Length + 1];
			Array.Copy(entityFilter, result, entityFilter.Length);
			result[result.Length - 1] = new TypedValue((int)DxfCode.LayoutName, layoutName);
			return result;
		}

		private static void AddFrame(
			List<PlotFrame> frames,
			Transaction tr,
			ObjectId id,
			PlotHelper.PlotSettingsData settings,
			FrameScanCache cache,
			string layoutName = "",
			Entity loadedEntity = null)
		{
			if (id.IsErased || id.IsNull) return;
			Entity ent = loadedEntity ?? tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
			if (ent == null || ent.IsErased) return;

			// FastSetVisibility pattern: skip hidden / layer-off / layer-frozen objects
			if (!ent.Visible) return;
			if (!IsLayerVisible(ent.LayerId, tr, cache)) return;

			Extents3d extents;
			if (settings.FrameType == PlotHelper.FrameType.Block)
			{
				BlockReference br = ent as BlockReference;
				if (br == null) return;
				string name = GetBlockName(br, tr, cache);
				if (!cache.FrameNames.Contains(name)) return;
				if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr, cache);
				try { extents = CalculateBlockExtents(br, tr, cache); }
				catch { return; } // Block không có extents hợp lệ
			}
			else
			{
				Polyline pl = ent as Polyline;
				if (pl == null) return;
				if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr, cache);
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

		private static string GetBlockName(BlockReference br, Transaction tr, FrameScanCache cache)
		{
			if (br.IsDynamicBlock)
			{
				if (cache.BlockNames.TryGetValue(br.DynamicBlockTableRecord, out string blockName))
					return blockName;

				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
				blockName = btr.Name;
				cache.BlockNames[br.DynamicBlockTableRecord] = blockName;
				return blockName;
			}
			return br.Name;
		}

		private static bool IsLayerVisible(ObjectId layerId, Transaction tr, FrameScanCache cache)
		{
			if (cache.VisibleLayers.TryGetValue(layerId, out bool isVisible))
				return isVisible;

			// Preserve the previous fail-open behavior when layer metadata is unavailable.
			isVisible = true;
			try
			{
				LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
				isVisible = !layer.IsOff && !layer.IsFrozen;
			}
			catch { }

			cache.VisibleLayers[layerId] = isVisible;
			return isVisible;
		}

		private static string GetLayoutName(ObjectId ownerId, Transaction tr, FrameScanCache cache)
		{
			if (cache.LayoutNames.TryGetValue(ownerId, out string layoutName))
				return layoutName;

			BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ownerId, OpenMode.ForRead);
			if (btr.IsLayout)
			{
				Layout lay = (Layout)tr.GetObject(btr.LayoutId, OpenMode.ForRead);
				layoutName = lay.LayoutName;
			}
			else
			{
				layoutName = "Model";
			}

			cache.LayoutNames[ownerId] = layoutName;
			return layoutName;
		}

		/// <summary>
		/// Tính Extents của BlockReference mà bỏ qua phần DefinedWidth dư thừa của MText.
		/// Duyệt từng entity con trong Block Definition, với MText thì dùng ActualWidth/ActualHeight
		/// thay vì GeometricExtents (bị ảnh hưởng bởi DefinedWidth).
		/// </summary>
		private static Extents3d CalculateBlockExtents(BlockReference br, Transaction tr, FrameScanCache cache)
		{
			// DynamicBlockTableRecord is the authoring definition. BlockTableRecord is the
			// evaluated anonymous definition for this reference and reflects its parameters.
			ObjectId definitionId = br.BlockTableRecord;

			if (!cache.BlockDefinitionExtents.TryGetValue(definitionId, out List<Extents3d> definitionExtents))
			{
				definitionExtents = new List<Extents3d>();
				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);

				foreach (ObjectId entId in btr)
				{
					Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
					if (ent == null || ent.IsErased || !ent.Visible) continue;
					if (!IsLayerVisible(ent.LayerId, tr, cache)) continue;

					try
					{
						definitionExtents.Add(ent is MText mt && mt.Width > 0 && mt.ActualWidth < mt.Width
							? CalculateMTextActualExtents(mt)
							: ent.GeometricExtents);
					}
					catch { }
				}

				cache.BlockDefinitionExtents[definitionId] = definitionExtents;
			}

			Extents3d? combinedExtents = null;
			Matrix3d blockTransform = br.BlockTransform;

			// Reuse definition-space extents; only the instance transform changes.
			foreach (Extents3d localExtents in definitionExtents)
			{
				Extents3d entExtents = localExtents;
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

			PlotHelper.SortOrder primaryOrder = settings.GroupOrder;
			bool primaryIsHorizontal = primaryOrder == PlotHelper.SortOrder.LeftToRight || primaryOrder == PlotHelper.SortOrder.RightToLeft;
			double fuzz = Math.Max(0, settings.Fuzz);
			var sortableFrames = new List<SortFrame>(frames.Count);
			for (int index = 0; index < frames.Count; index++)
			{
				PlotFrame frame = frames[index];
				sortableFrames.Add(new SortFrame
				{
					Frame = frame,
					BasePoint = frame.GetBasePoint(settings.SortBasePoint),
					OriginalIndex = index
				});
			}

			if (settings.CrossGroupOrder != PlotHelper.SortOrder.None)
			{
				// Form deterministic rows/columns first. Comparing every pair with a fuzzy
				// tolerance is not transitive and can make List.Sort produce unstable output.
				var byCrossAxis = new List<SortFrame>(sortableFrames);
				byCrossAxis.Sort((left, right) =>
				{
					double leftAxis = primaryIsHorizontal ? left.BasePoint.Y : left.BasePoint.X;
					double rightAxis = primaryIsHorizontal ? right.BasePoint.Y : right.BasePoint.X;
					int axisComparison = leftAxis.CompareTo(rightAxis);
					return axisComparison != 0
						? axisComparison
						: left.OriginalIndex.CompareTo(right.OriginalIndex);
				});

				double? groupAnchor = null;
				int groupIndex = -1;
				foreach (SortFrame item in byCrossAxis)
				{
					double crossAxis = primaryIsHorizontal ? item.BasePoint.Y : item.BasePoint.X;
					if (!groupAnchor.HasValue || Math.Abs(crossAxis - groupAnchor.Value) > fuzz)
					{
						groupAnchor = crossAxis;
						groupIndex++;
					}
					item.GroupIndex = groupIndex;
				}
			}

			sortableFrames.Sort((left, right) =>
			{
				if (settings.CrossGroupOrder != PlotHelper.SortOrder.None)
				{
					int groupComparison = left.GroupIndex.CompareTo(right.GroupIndex);
					if (groupComparison != 0)
					{
						bool descendingCrossAxis = primaryIsHorizontal
							? settings.CrossGroupOrder == PlotHelper.SortOrder.TopToBottom
							: settings.CrossGroupOrder != PlotHelper.SortOrder.LeftToRight;
						return descendingCrossAxis ? -groupComparison : groupComparison;
					}
				}

				double leftPrimary = primaryIsHorizontal ? left.BasePoint.X : left.BasePoint.Y;
				double rightPrimary = primaryIsHorizontal ? right.BasePoint.X : right.BasePoint.Y;
				int primaryComparison = leftPrimary.CompareTo(rightPrimary);
				bool descendingPrimary = primaryOrder == PlotHelper.SortOrder.RightToLeft || primaryOrder == PlotHelper.SortOrder.TopToBottom;
				if (primaryComparison != 0)
					return descendingPrimary ? -primaryComparison : primaryComparison;

				return left.OriginalIndex.CompareTo(right.OriginalIndex);
			});

			frames.Clear();
			foreach (SortFrame item in sortableFrames)
				frames.Add(item.Frame);
		}

		private static void PlotSinglePage(PlotInfo plotInfo, Document doc, bool plotToFile, string outputPath)
		{
			using var engine = PlotFactory.CreatePublishEngine();
			bool plotStarted = false;
			bool documentStarted = false;
			bool pageStarted = false;
			bool graphicsStarted = false;

			try
			{
				engine.BeginPlot(null, null);
				plotStarted = true;

				engine.BeginDocument(plotInfo, doc.Name, null, 1, plotToFile, plotToFile ? outputPath : null);
				documentStarted = true;

				engine.BeginPage(new PlotPageInfo(), plotInfo, true, null);
				pageStarted = true;

				engine.BeginGenerateGraphics(null);
				graphicsStarted = true;
				engine.EndGenerateGraphics(null);
				graphicsStarted = false;

				engine.EndPage(null);
				pageStarted = false;
				engine.EndDocument(null);
				documentStarted = false;
				engine.EndPlot(null);
				plotStarted = false;
			}
			finally
			{
				// Printer drivers can throw during any plotting stage. Close every started stage
				// so AutoCAD does not remain in a busy plot state and block the next print job.
				if (graphicsStarted) { try { engine.EndGenerateGraphics(null); } catch { } }
				if (pageStarted) { try { engine.EndPage(null); } catch { } }
				if (documentStarted) { try { engine.EndDocument(null); } catch { } }
				if (plotStarted) { try { engine.EndPlot(null); } catch { } }
			}
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

			if (string.IsNullOrWhiteSpace(settings.DeviceName) || string.IsNullOrWhiteSpace(settings.PaperSize))
			{
				ed.WriteMessage("\n[TPL] A plot device and paper size are required.");
				return;
			}

			if (isFilePrinter && string.IsNullOrWhiteSpace(settings.OutputPath))
			{
				ed.WriteMessage("\n[TPL] An output folder is required for file printers.");
				return;
			}

			string baseName = settings.BaseFileName;
			if (string.IsNullOrWhiteSpace(baseName)) baseName = "Drawing1";
			string ext = ".plt";
			string outDir = "";

			// Chỉ chuẩn bị file output nếu là máy in file
			if (isFilePrinter)
			{
				ext = settings.DeviceName.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0 ? ".pdf" : ".plt";
				outDir = settings.OutputPath;
				if (!outDir.EndsWith("\\")) outDir += "\\";
				if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
			}

			// Phase 1: pre-collect required layout/plot data inside ONE transaction
			// PlotEngine must NEVER run while a transaction is active.
			var plotJobs = new List<(string LayoutName, ObjectId LayoutId, bool ModelType, PlotSettings Ps, Extents3d Extents)>();
			try
			{
				using Transaction tr = db.TransactionManager.StartTransaction();
				PlotSettingsValidator psv = PlotSettingsValidator.Current;
				Matrix3d? modelWcsToDcs = null;
				string canonicalPaperSize = null;
				var layoutCache = new Dictionary<string, (ObjectId Id, Layout Layout)>(StringComparer.OrdinalIgnoreCase);
				foreach (var frame in frames)
				{
					if (!layoutCache.TryGetValue(frame.LayoutName, out var layoutData))
					{
						ObjectId layoutId = LayoutManager.Current.GetLayoutId(frame.LayoutName);
						Layout layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
						layoutData = (layoutId, layout);
						layoutCache.Add(frame.LayoutName, layoutData);
					}

					ObjectId layId = layoutData.Id;
					Layout lay = layoutData.Layout;
					PlotSettings ps = new(lay.ModelType);
					try
					{
						ps.CopyFrom(lay);

						// Configure the device before resolving the paper name. Physical printers
						// often expose localized media names that cannot be reconstructed safely.
						if (canonicalPaperSize == null)
						{
							psv.SetPlotConfigurationName(ps, settings.DeviceName, null);
							psv.RefreshLists(ps);
							canonicalPaperSize = PlotHelper.ResolveCanonicalPaperSize(psv, ps, settings.PaperSize);
							if (string.IsNullOrEmpty(canonicalPaperSize))
								throw new InvalidOperationException($"Paper size '{settings.PaperSize}' is not available on '{settings.DeviceName}'.");
						}
						psv.SetPlotConfigurationName(ps, settings.DeviceName, canonicalPaperSize);
						psv.RefreshLists(ps);

						// 2. Set PlotWindowArea (dummy or real) BEFORE PlotType
						Extents2d plotExt;
						if (lay.ModelType)
						{
							if (!modelWcsToDcs.HasValue)
							{
								using ViewTableRecord vtr = ed.GetCurrentView();
								modelWcsToDcs = Matrix3d.WorldToPlane(vtr.ViewDirection) *
												Matrix3d.Displacement(Point3d.Origin - vtr.Target) *
												Matrix3d.Rotation(vtr.ViewTwist, vtr.ViewDirection, vtr.Target);
							}
							Matrix3d matWCS2DCS = modelWcsToDcs.Value;

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

						if (!string.IsNullOrWhiteSpace(settings.PlotStyle))
							psv.SetCurrentStyleSheet(ps, settings.PlotStyle);

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
						ps = null;
					}
					finally
					{
						ps?.Dispose();
					}
				}
				tr.Commit();
			}
			catch (System.Exception ex)
			{
				foreach (var job in plotJobs)
				{
					try { job.Ps.Dispose(); } catch { }
				}
				ed.WriteMessage($"\nPlot data error: {ex.Message}");
				throw new InvalidOperationException($"Cannot configure plot device '{settings.DeviceName}': {ex.Message}", ex);
			}

			// Phase 2: PlotEngine + Regen with NO active transaction.
			// Every native PlotSettings and global state change is restored even if UI setup fails.
			string originalLayout = LayoutManager.Current.CurrentLayout;
			short bgPlot = 0;
			bool backgroundPlotDisabled = false;
			ProgressWindow progressWin = null;
			var pendingPlotSettings = new HashSet<PlotSettings>(plotJobs.Select(job => job.Ps));
			var generatedFiles = new List<string>();
			int errorCount = 0;
			string firstPlotError = null;
			try
			{
				bgPlot = (short)Application.GetSystemVariable("BACKGROUNDPLOT");
				Application.SetSystemVariable("BACKGROUNDPLOT", 0);
				backgroundPlotDisabled = true;

				progressWin = new ProgressWindow(L10n.T("prog_title"), plotJobs.Count);
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
							firstPlotError ??= "AutoCAD is still busy with another plot operation.";
							continue;
						}

						{
							// ═══ Unified Plot Engine (Silent Plotting) ═══
							var plotInfo = new PlotInfo { Layout = LayoutId, OverrideSettings = Ps };
							var plotInfoValidator = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
							plotInfoValidator.Validate(plotInfo);

							PlotSinglePage(plotInfo, doc, isFilePrinter, filePath);
							if (isFilePrinter) generatedFiles.Add(filePath);
						}
					}
					catch (System.Exception plotEx)
					{
						// ═══ CRASH-PROOF: bắt lỗi từng trang, không crash toàn batch ═══
						errorCount++;
						firstPlotError ??= plotEx.Message;
						ed.WriteMessage($"\n[TPL] ERROR page {i + 1}: {plotEx.GetType().Name}: {plotEx.Message}");
					}
					finally
					{
						try { Ps.Dispose(); }
						finally { pendingPlotSettings.Remove(Ps); }
					}
				}

				// Thông báo tổng kết lỗi
				if (errorCount > 0)
				{
					ed.WriteMessage($"\n[TPL] Completed with {errorCount} error(s) out of {plotJobs.Count} page(s).");
					if (!isFilePrinter)
						throw new InvalidOperationException($"Physical printer '{settings.DeviceName}' could not print {errorCount} page(s): {firstPlotError}");
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
						if (!PdfEditorLauncher.TryLaunch(
							generatedFiles,
							baseName,
							deleteSourcesOnExit: true,
							out string launchError))
						{
							ed.WriteMessage($"\n[TPL] PDF Editor could not be started: {launchError}");
						}
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
				foreach (var ps in pendingPlotSettings)
				{
					try { ps.Dispose(); } catch { }
				}
				if (backgroundPlotDisabled)
				{
					try { Application.SetSystemVariable("BACKGROUNDPLOT", bgPlot); } catch { }
				}
				try
				{
					if (!string.IsNullOrEmpty(originalLayout))
						LayoutManager.Current.CurrentLayout = originalLayout;
					ed.UpdateScreen();
				}
				catch { }
			}
		}
	}
}
