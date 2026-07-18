using System;
using System.Collections.Generic;
using System.Linq;
using Prima.VinaCAD.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace TPL
{
	public partial class MainWindow
	{
		// ── Global doc events (STATIC — phải crash-proof) ──
		private static void GlobalDocumentActivated(object sender, DocumentCollectionEventArgs e)
		{
			try
			{
				if (e?.Document == null || e.Document.IsDisposed) return;
				if (!_pendingTransients.TryGetValue(e.Document, out var list)) return;
				var tm = TransientManager.CurrentTransientManager;
				if (tm == null) return;
				foreach (var obj in list)
				{ try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { } }
				_pendingTransients.Remove(e.Document);
			}
			catch { }
		}

		private static void GlobalDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
		{
			try { if (e?.Document != null) _pendingTransients.Remove(e.Document); } catch { }
		}

		// ── Markers ──
		private void DrawMarkersIfNeeded(List<PlotFrame> frames)
		{
			if (frames == null || frames.Count == 0) return;
			for (int i = 0; i < frames.Count; i++)
			{
				frames[i].OrderIndex = i + 1;
				frames[i].MarkerText = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
			if (chkMark.IsChecked == true) { ClearTransientMarkers(); DrawPermanentMarkers(frames); }
			else { ClearPermanentMarkers(); DrawTransientMarkers(frames); }
		}

		private void ClearTransientMarkers()
		{
			try
			{
				if (transientObjects.Count == 0) return;
				Document currentDoc = Application.DocumentManager.MdiActiveDocument;
				if (_markerDoc != null && currentDoc != null && _markerDoc != currentDoc && !_markerDoc.IsDisposed)
				{
					if (!_pendingTransients.ContainsKey(_markerDoc)) _pendingTransients[_markerDoc] = new List<DBObject>();
					_pendingTransients[_markerDoc].AddRange(transientObjects);
					transientObjects.Clear(); return;
				}
				if (currentDoc != null)
				{
					using var docLock = new SafeDocLock(currentDoc);
					var tm = TransientManager.CurrentTransientManager;
					foreach (var obj in transientObjects)
					{ try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { } }
				}
				transientObjects.Clear();
			}
			catch { transientObjects.Clear(); }
		}

		private void ClearPermanentMarkers()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null || doc.IsDisposed) return;
			try
			{
				using (var docLock = new SafeDocLock(doc))
				using (var tr = doc.Database.TransactionManager.StartTransaction())
				{
					var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
					foreach (ObjectId btrId in bt)
					{
						var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
						if (btr.IsLayout)
							foreach (ObjectId entId in btr)
							{
								try
								{
									var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
									if (ent != null && string.Equals(ent.Layer, "TPL_MARKERS", StringComparison.OrdinalIgnoreCase))
									{ ent.UpgradeOpen(); ent.Erase(); }
								}
								catch { }
							}
					}
					tr.Commit();
				}
				doc.Editor.UpdateScreen();
			}
			catch { }
		}

		private void DrawTransientMarkers(List<PlotFrame> frames)
		{
			ClearTransientMarkers();
			string previewStage = "initializing";
			try
			{
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null) return;
				previewStage = "locking document";
				using var docLock = new SafeDocLock(doc);
				previewStage = "reading current layout";
				string curLayout = LayoutManager.Current.CurrentLayout;
				previewStage = "getting TransientManager";
				var tm = TransientManager.CurrentTransientManager;
				if (tm == null) throw new InvalidOperationException("VinaCAD TransientManager is unavailable.");

				// Dùng IntegerCollection rỗng = vẽ ở tất cả viewports
				var viewportNumbers = new IntegerCollection();
				var drawingMode = TransientDrawingMode.Main;
				// Dùng subDrawingMode cố định — GetFreeSubDrawingMode trả về
				// eNotImplementedYet trên VinaCAD 2026, và giá trị tăng liên tục
				// gây fail sau nhiều lần gọi.
				int subDrawingMode = 128;

				int markerCount = 0;
				_markerDoc = doc;
				for (int i = 0; i < frames.Count; i++)
				{
					var frame = frames[i];
					if (!string.Equals(frame.LayoutName, curLayout, StringComparison.OrdinalIgnoreCase)) continue;
					double lenX = frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X;
					double lenY = frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y;
					if (lenX <= 0 || lenY <= 0) continue;

					var txt = new MText();
					previewStage = $"creating marker text {frame.MarkerText}";
					txt.SetDatabaseDefaults(doc.Database);
					txt.Contents = frame.MarkerText;
					txt.TextHeight = Math.Min(lenX, lenY) / 5.0;
					txt.Location = new Point3d(
						(frame.Extents.MinPoint.X + frame.Extents.MaxPoint.X) / 2,
						(frame.Extents.MinPoint.Y + frame.Extents.MaxPoint.Y) / 2, 0);
					txt.Attachment = AttachmentPoint.MiddleCenter;
					txt.ColorIndex = 1;
					previewStage = $"adding marker text {frame.MarkerText}";
					bool textAdded = tm.AddTransient(txt, drawingMode, subDrawingMode, viewportNumbers);
					if (!textAdded)
					{
						txt.Dispose();
						doc.Editor.WriteMessage($"\n[TPL] Transient text {frame.MarkerText} rejected by VinaCAD.");
						continue;
					}
					transientObjects.Add(txt);

					previewStage = $"creating marker line {frame.MarkerText}";
					var line = new Line(
						new Point3d(frame.Extents.MinPoint.X, frame.Extents.MaxPoint.Y, 0),
						new Point3d(frame.Extents.MaxPoint.X, frame.Extents.MinPoint.Y, 0));
					line.SetDatabaseDefaults(doc.Database);
					line.ColorIndex = 1;
					previewStage = $"adding marker line {frame.MarkerText}";
					bool lineAdded = tm.AddTransient(line, drawingMode, subDrawingMode, viewportNumbers);
					if (!lineAdded)
					{
						line.Dispose();
						doc.Editor.WriteMessage($"\n[TPL] Transient line {frame.MarkerText} rejected by VinaCAD.");
						continue;
					}
					transientObjects.Add(line);
					markerCount++;
				}
				previewStage = "refreshing screen";
				doc.Editor.UpdateScreen();
				if (markerCount == 0)
				{
					doc.Editor.WriteMessage(
						$"\n[TPL] No transient marker matched current layout '{curLayout}' " +
						$"(frame layouts: {string.Join(", ", frames.ConvertAll(frame => frame.LayoutName).Distinct(StringComparer.OrdinalIgnoreCase))}).");
				}
				else
				{
					doc.Editor.WriteMessage(
						$"\n[TPL] Added {markerCount} transient marker(s) on all viewports.");
				}
			}
			catch (System.Exception ex)
			{
				try
				{
					Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
						$"\n[TPL] Marker preview error at {previewStage}: {ex.Message}");
				}
				catch { }
				ClearTransientMarkers();
			}
		}

		private void DrawPermanentMarkers(List<PlotFrame> frames)
		{
			ClearPermanentMarkers();
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return;
			try
			{
				using (var docLock = new SafeDocLock(doc))
				using (var tr = doc.Database.TransactionManager.StartTransaction())
				{
					var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
					if (!lt.Has("TPL_MARKERS"))
					{
						lt.UpgradeOpen();
						var ltr = new LayerTableRecord
						{
							Name = "TPL_MARKERS",
							IsPlottable = false,
							Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 1)
						};
						lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
					}
					else
					{
						var markerLayer = (LayerTableRecord)tr.GetObject(lt["TPL_MARKERS"], OpenMode.ForWrite);
						markerLayer.IsOff = false;
						markerLayer.IsFrozen = false;
						markerLayer.IsLocked = false;
						markerLayer.IsPlottable = false;
					}
					string curLayout = LayoutManager.Current.CurrentLayout;
					var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
					for (int i = 0; i < frames.Count; i++)
					{
						var frame = frames[i];
						if (frame.LayoutName != curLayout) continue;
						double lenX = frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X;
						double lenY = frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y;
						if (lenX <= 0 || lenY <= 0) continue;

						var line = new Line(new Point3d(frame.Extents.MinPoint.X, frame.Extents.MaxPoint.Y, 0), new Point3d(frame.Extents.MaxPoint.X, frame.Extents.MinPoint.Y, 0))
						{
							Layer = "TPL_MARKERS",
							ColorIndex = 1
						};
						btr.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);

						var txt = new MText
						{
							Layer = "TPL_MARKERS",
							Contents = frame.MarkerText,
							TextHeight = Math.Min(lenX, lenY) / 5.0,
							Location = new Point3d((frame.Extents.MinPoint.X + frame.Extents.MaxPoint.X) / 2, (frame.Extents.MinPoint.Y + frame.Extents.MaxPoint.Y) / 2, 0),
							Attachment = AttachmentPoint.MiddleCenter,
							ColorIndex = 1
						};
						btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
					}
					tr.Commit();
				}
				doc.Editor.Regen();
			}
			catch (System.Exception ex)
			{
				try { doc.Editor.WriteMessage($"\n[TPL] Permanent marker error: {ex.Message}"); } catch { }
			}
		}

		// ── Cleanup on close ──
		private void QueueTransientsForCleanup()
		{
			try
			{
				var tm = TransientManager.CurrentTransientManager;
				Document currentDoc = Application.DocumentManager.MdiActiveDocument;
				if (transientObjects.Count > 0 && _markerDoc != null && !_markerDoc.IsDisposed)
				{
					if (_markerDoc == currentDoc)
					{
						try
						{
							using var docLock = new SafeDocLock(_markerDoc);
							foreach (var obj in transientObjects)
							{ try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { } }
						}
						catch { }
					}
					else
					{
						if (!_pendingTransients.ContainsKey(_markerDoc)) _pendingTransients[_markerDoc] = new List<DBObject>();
						_pendingTransients[_markerDoc].AddRange(transientObjects);
					}
					transientObjects.Clear();
				}
				if (currentDoc != null && _pendingTransients.TryGetValue(currentDoc, out var list))
				{
					try
					{
						using var docLock = new SafeDocLock(currentDoc);
						foreach (var obj in list)
						{ try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { } }
					}
					catch { }
					_pendingTransients.Remove(currentDoc);
				}
			}
			catch { }
		}

		private void ClearPermanentMarkersGlobally()
		{
			foreach (Document doc in Application.DocumentManager)
			{
				if (doc == null || doc.IsDisposed) continue;
				try
				{
					using (var docLock = new SafeDocLock(doc))
					using (var tr = doc.Database.TransactionManager.StartTransaction())
					{
						var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
						foreach (ObjectId btrId in bt)
						{
							var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
							if (btr.IsLayout)
								foreach (ObjectId entId in btr)
								{
									try
									{
										var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
										if (ent != null && string.Equals(ent.Layer, "TPL_MARKERS", StringComparison.OrdinalIgnoreCase))
										{ ent.UpgradeOpen(); ent.Erase(); }
									}
									catch { }
								}
						}
						tr.Commit();
					}
					try { doc.Editor.UpdateScreen(); } catch { }
				}
				catch { }
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			_previewDebounce?.Stop();
			QueueTransientsForCleanup();
			ClearPermanentMarkersGlobally();
			try { UnsubscribeDatabaseEvents(); } catch { }
			base.OnClosed(e);
		}
	}
}
