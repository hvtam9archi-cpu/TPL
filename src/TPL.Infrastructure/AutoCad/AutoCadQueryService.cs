using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TPL.Domain.Enums;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;
// Alias to avoid clash with Autodesk.AutoCAD.EditorInput.SelectionMode
using DomainSelectionMode = TPL.Domain.Enums.SelectionMode;

namespace TPL.Infrastructure.AutoCad
{
	/// <summary>
	/// Truy vấn dữ liệu bản vẽ AutoCAD (read-only).
	/// Trích xuất từ CoreLogic.cs (PlotHelper) và PlotLogic.cs (SelectFrames).
	/// </summary>
	public class AutoCadQueryService : IDrawingQueryService
	{
		/// <summary>Ngưỡng tối thiểu cho chiều rộng/cao của plot frame.</summary>
		private const double MinFrameDimension = 0.001;

		public List<string> GetPrinters()
		{
			PlotSettingsValidator psv = PlotSettingsValidator.Current;
			return psv.GetPlotDeviceList().Cast<string>().ToList();
		}

		public List<string> GetPaperSizes(string deviceName)
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return new List<string>();
			Database db = doc.Database;
			List<string> papers = new();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				using (PlotSettings ps = new(db.TileMode))
				{
					try
					{
						PlotSettingsValidator psv = PlotSettingsValidator.Current;
						psv.SetPlotConfigurationName(ps, deviceName, null);
						psv.RefreshLists(ps);
						papers = psv.GetCanonicalMediaNameList(ps)
							.Cast<string>()
							.Select(p => p.Replace("_", " "))
							.ToList();
					}
					catch { }
				}
				tr.Commit();
			}
			return papers;
		}

		public List<string> GetPlotStyles()
		{
			PlotSettingsValidator psv = PlotSettingsValidator.Current;
			return psv.GetPlotStyleSheetList().Cast<string>().ToList();
		}

		public List<string> GetBlockNames()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return new List<string>();
			Database db = doc.Database;
			List<string> blocks = new();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
				foreach (ObjectId btrId in bt)
				{
					BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
					if (!btr.IsAnonymous && !btr.IsLayout)
						blocks.Add(btr.Name);
				}
				tr.Commit();
			}
			blocks.Sort();
			return blocks;
		}

		public List<string> GetLayerNames()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return new List<string>();
			Database db = doc.Database;
			List<string> layers = new();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
				foreach (ObjectId ltrId in lt)
				{
					LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(ltrId, OpenMode.ForRead);
					layers.Add(ltr.Name);
				}
				tr.Commit();
			}
			layers.Sort();
			return layers;
		}

		public bool IsFilePrinter(string deviceName)
		{
			if (string.IsNullOrEmpty(deviceName)) return true;
			string lower = deviceName.ToLower();
			string[] fileKeywords = { "pdf", "dwf", "dwg", "png", "jpg", "jpeg", "tiff", "svg", "eps", "plt",
				"publish to web", "dwfx", "design review" };
			return fileKeywords.Any(k => lower.Contains(k));
		}

		public List<PlotFrame> SelectFrames(PlotSettingsData settings)
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return new List<PlotFrame>();
			Database db = doc.Database;
			Editor ed = doc.Editor;
			List<PlotFrame> frames = new();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				if (settings.SelectionMode == DomainSelectionMode.Manual)
				{
					if (settings.ManualSelectionHandles != null)
					{
						foreach (long handle in settings.ManualSelectionHandles)
						{
							try
							{
								Handle h = new Handle(handle);
								ObjectId id = db.GetObjectId(false, h, 0);
								if (id.IsErased || id.IsNull) continue;
								AddFrame(frames, tr, id, settings);
							}
							catch { }
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

							if (settings.SelectionMode == DomainSelectionMode.CurrentLayout && entLayout != currentLayout)
								continue;

							AddFrame(frames, tr, id, settings, entLayout);
						}
					}
				}
				tr.Commit();
			}

			return frames;
		}

		public string GetCurrentDrawingName()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return "Drawing1";
			return System.IO.Path.GetFileNameWithoutExtension(doc.Name);
		}

		public string GetCurrentLayoutName()
		{
			return LayoutManager.Current.CurrentLayout;
		}

		// ─── Private Helpers ─────────────────────────────────────────────

		private static TypedValue[] GetFilter(PlotSettingsData settings)
		{
			if (settings.FrameType == FrameType.Block)
			{
				string blockNameFilter = string.Join(",", settings.FrameNames);
				if (!string.IsNullOrEmpty(blockNameFilter))
				{
					blockNameFilter += ",`*U*";
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

		private void AddFrame(List<PlotFrame> frames, Transaction tr, ObjectId id, PlotSettingsData settings, string layoutName = "")
		{
			if (id.IsErased || id.IsNull) return;
			Entity ent = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
			if (ent == null || ent.IsErased) return;
			if (!ent.Visible) return;

			try
			{
				LayerTableRecord layer = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
				if (layer.IsOff || layer.IsFrozen) return;
			}
			catch { }

			Extents3d extents;
			if (settings.FrameType == FrameType.Block)
			{
				BlockReference br = ent as BlockReference;
				if (br == null) return;
				string name = GetBlockName(br, tr);
				if (!settings.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase))) return;
				if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr);
				try { extents = CalculateBlockExtents(br, tr); }
				catch { return; }
			}
			else
			{
				Polyline pl = ent as Polyline;
				if (pl == null) return;
				if (string.IsNullOrEmpty(layoutName)) layoutName = GetLayoutName(ent.OwnerId, tr);
				try { extents = pl.GeometricExtents; }
				catch { return; }
			}

			double frameWidth = Math.Abs(extents.MaxPoint.X - extents.MinPoint.X);
			double frameHeight = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
			if (frameWidth < MinFrameDimension || frameHeight < MinFrameDimension) return;

			frames.Add(new PlotFrame
			{
				Handle = id.Handle.Value,
				MinX = extents.MinPoint.X,
				MinY = extents.MinPoint.Y,
				MaxX = extents.MaxPoint.X,
				MaxY = extents.MaxPoint.Y,
				LayoutName = layoutName
			});
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
						entExtents = CalculateMTextActualExtents(mt);
					else
						entExtents = ent.GeometricExtents;
				}
				catch { continue; }

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

			return combinedExtents ?? br.GeometricExtents;
		}

		/// <summary>
		/// Tính bounding box thực tế của MText dựa trên ActualWidth/ActualHeight.
		/// </summary>
		private static Extents3d CalculateMTextActualExtents(MText mt)
		{
			double w = mt.ActualWidth;
			double h = mt.ActualHeight;
			Point3d loc = mt.Location;

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
	}
}
