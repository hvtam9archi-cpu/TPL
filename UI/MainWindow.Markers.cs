using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	public partial class MainWindow
	{
		private const string MarkerLayerName = "TPL_MARKERS";
		private const string MarkerRegAppName = "TPL_MARKER";
		private const string MarkerTag = "TPL_MARKER_V1";

		private static void EnsureMarkerRegApp(Transaction tr, Database db)
		{
			var regApps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
			if (regApps.Has(MarkerRegAppName)) return;

			regApps.UpgradeOpen();
			var regApp = new RegAppTableRecord { Name = MarkerRegAppName };
			regApps.Add(regApp);
			tr.AddNewlyCreatedDBObject(regApp, true);
		}

		private static void TagMarker(Entity entity)
		{
			using var data = new ResultBuffer(
				new TypedValue((int)DxfCode.ExtendedDataRegAppName, MarkerRegAppName),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, MarkerTag));
			entity.XData = data;
		}

		private static bool IsTaggedMarker(Entity entity)
		{
			try
			{
				using ResultBuffer data = entity.GetXDataForApplication(MarkerRegAppName);
				return data != null && data.AsArray().Any(value =>
					value.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
					string.Equals(value.Value as string, MarkerTag, StringComparison.Ordinal));
			}
			catch { return false; }
		}

		// ── Global doc events (STATIC — phải crash-proof) ──
		internal static void SubscribeGlobalMarkerEvents()
		{
			if (_globalEventsSubscribed) return;

			Application.DocumentManager.DocumentToBeDestroyed += GlobalDocumentToBeDestroyed;
			_globalEventsSubscribed = true;
		}

		internal static void UnsubscribeGlobalMarkerEvents()
		{
			if (!_globalEventsSubscribed) return;

			Commands.MainFormInstance?.ClearTransientMarkers();
			Application.DocumentManager.DocumentToBeDestroyed -= GlobalDocumentToBeDestroyed;
			_globalEventsSubscribed = false;
		}

		private static void GlobalDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
		{
			try
			{
				MainWindow window = Commands.MainFormInstance;
				if (e?.Document == null || window == null || window._markerDoc != e.Document) return;
				window.ClearTransientMarkers();
				window._markerDoc = null;
			}
			catch (Autodesk.AutoCAD.Runtime.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] DocumentToBeDestroyed transient cleanup: {ex}");
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] DocumentToBeDestroyed transient cleanup: {ex}");
			}
		}

		// ── Markers ──
		private void DrawMarkersIfNeeded(List<PlotFrame> frames)
		{
			if (frames == null || frames.Count == 0)
			{
				ClearTransientMarkers();
				ClearPermanentMarkers();
				return;
			}
			if (chkMark.IsChecked == true) { ClearTransientMarkers(); DrawPermanentMarkers(frames); }
			else { ClearPermanentMarkers(); DrawTransientMarkers(frames); }
		}

		private void ClearTransientMarkers()
		{
			if (transientObjects.Count == 0)
			{
				_markerDoc = null;
				return;
			}

			try
			{
				// These drawables are owned in memory and are never opened from a Database.
				// Remove each drawable from every viewport before releasing its native object,
				// including when its drawing is inactive or is about to be destroyed.
				var manager = TransientManager.CurrentTransientManager;
				if (manager == null) return;
				for (int index = transientObjects.Count - 1; index >= 0; index--)
				{
					DBObject drawable = transientObjects[index];
					try
					{
						if (drawable != null && !drawable.IsDisposed)
						{
							manager.EraseTransient(drawable, new IntegerCollection());
							drawable.Dispose();
						}
						transientObjects.RemoveAt(index);
					}
					catch (Autodesk.AutoCAD.Runtime.Exception ex)
					{
						System.Diagnostics.Trace.WriteLine($"[TPL] Cannot erase transient marker {index}: {ex}");
					}
					catch (System.Exception ex)
					{
						System.Diagnostics.Trace.WriteLine($"[TPL] Cannot release transient marker {index}: {ex}");
					}
				}
				if (transientObjects.Count == 0) _markerDoc = null;
			}
			catch (System.Exception ex)
			{
				// Keep ownership so a later cleanup can retry after a graphics-system error.
				System.Diagnostics.Trace.WriteLine($"[TPL] Transient marker cleanup: {ex}");
			}
		}

		private void ClearPermanentMarkers()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null || doc.IsDisposed) return;
			ClearPermanentMarkers(doc);
		}

		private void ClearPermanentMarkers(Document doc)
		{
			if (doc == null || doc.IsDisposed) return;

			Database db = doc.Database;
			if (!_permanentMarkerIds.TryGetValue(db, out List<ObjectId> markerIds))
			{
				markerIds = new List<ObjectId>();
				_permanentMarkerIds[db] = markerIds;
			}

			bool scanForUntrackedMarkers = !_initializedMarkerCleanup.Contains(db);
			if (!scanForUntrackedMarkers && markerIds.Count == 0) return;

			try
			{
				using (var docLock = doc.LockDocument())
				using (var tr = db.TransactionManager.StartTransaction())
				{
					if (scanForUntrackedMarkers)
					{
						// Clean persisted markers once per database. Subsequent refreshes erase
						// only the ObjectIds created by this window.
						var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
						foreach (ObjectId btrId in bt)
						{
							var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
							if (!btr.IsLayout) continue;

							foreach (ObjectId entId in btr)
							{
								try
								{
									var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
									if (ent != null && IsTaggedMarker(ent))
									{ ent.UpgradeOpen(); ent.Erase(); }
								}
								catch { }
							}
						}
					}
					else
					{
						foreach (ObjectId markerId in markerIds)
						{
							try
							{
								if (markerId.IsNull || markerId.IsErased) continue;
								var ent = tr.GetObject(markerId, OpenMode.ForWrite, false, true) as Entity;
								if (ent != null && !ent.IsErased) ent.Erase();
							}
							catch { }
						}
					}
					tr.Commit();
				}
				markerIds.Clear();
				_initializedMarkerCleanup.Add(db);
				doc.Editor.UpdateScreen();
			}
			catch { }
		}

		private void DrawTransientMarkers(List<PlotFrame> frames)
		{
			ClearTransientMarkers();
			try
			{
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null) return;
				using var docLock = doc.LockDocument();
				string curLayout = LayoutManager.Current.CurrentLayout;
				var tm = TransientManager.CurrentTransientManager;
				_markerDoc = doc;
				for (int i = 0; i < frames.Count; i++)
				{
					var frame = frames[i];
					if (frame.LayoutName != curLayout) continue;
					double lenX = frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X;
					double lenY = frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y;
					if (lenX <= 0 || lenY <= 0) continue;

					var txt = new MText
					{
						Contents = "{\\fVerdana|b0|i0|c0|p0;" + (i + 1) + "}",
						TextHeight = Math.Min(lenX, lenY) / 5.0,
						Location = new Point3d((frame.Extents.MinPoint.X + frame.Extents.MaxPoint.X) / 2, (frame.Extents.MinPoint.Y + frame.Extents.MaxPoint.Y) / 2, 0),
						Attachment = AttachmentPoint.MiddleCenter,
						ColorIndex = 1
					};
					transientObjects.Add(txt);
					tm.AddTransient(txt, TransientDrawingMode.Main, 128, new IntegerCollection());

					var line = new Line(new Point3d(frame.Extents.MinPoint.X, frame.Extents.MaxPoint.Y, 0), new Point3d(frame.Extents.MaxPoint.X, frame.Extents.MinPoint.Y, 0))
					{
						ColorIndex = 1
					};
					transientObjects.Add(line);
					tm.AddTransient(line, TransientDrawingMode.Main, 128, new IntegerCollection());
				}
				doc.Editor.UpdateScreen();
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[TPL] Draw transient markers: {ex}");
				ClearTransientMarkers();
			}
		}

		private void DrawPermanentMarkers(List<PlotFrame> frames)
		{
			ClearPermanentMarkers();
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return;
			var newMarkerIds = new List<ObjectId>();
			try
			{
				using (var docLock = doc.LockDocument())
				using (var tr = doc.Database.TransactionManager.StartTransaction())
				{
					var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
					if (!lt.Has(MarkerLayerName))
					{
						lt.UpgradeOpen();
						var ltr = new LayerTableRecord
						{
							Name = MarkerLayerName,
							IsPlottable = false,
							Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1)
						};
						lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
					}
					EnsureMarkerRegApp(tr, doc.Database);
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
							Layer = MarkerLayerName,
							ColorIndex = 1
						};
						TagMarker(line);
						btr.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
						newMarkerIds.Add(line.ObjectId);

						var txt = new MText
						{
							Layer = MarkerLayerName,
							Contents = "{\\fVerdana|b0|i0|c0|p0;" + (i + 1) + "}",
							TextHeight = Math.Min(lenX, lenY) / 5.0,
							Location = new Point3d((frame.Extents.MinPoint.X + frame.Extents.MaxPoint.X) / 2, (frame.Extents.MinPoint.Y + frame.Extents.MaxPoint.Y) / 2, 0),
							Attachment = AttachmentPoint.MiddleCenter,
							ColorIndex = 1
						};
						TagMarker(txt);
						btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
						newMarkerIds.Add(txt.ObjectId);
					}
					tr.Commit();
				}
				_permanentMarkerIds[doc.Database].AddRange(newMarkerIds);
				doc.Editor.UpdateScreen();
			}
			catch { }
		}

		// ── Cleanup on close ──
		private void ClearTrackedPermanentMarkers()
		{
			foreach (Document doc in Application.DocumentManager)
			{
				if (doc != null && !doc.IsDisposed && _permanentMarkerIds.ContainsKey(doc.Database))
					ClearPermanentMarkers(doc);
			}

			_permanentMarkerIds.Clear();
			_initializedMarkerCleanup.Clear();
		}

		protected override void OnClosed(EventArgs e)
		{
			_previewDebounce?.Stop();
			_previewRefreshQueued = false;
			ResetFrameCache();
			ClearTransientMarkers();
			ClearTrackedPermanentMarkers();
			try { UnsubscribeDatabaseEvents(); } catch { }
			base.OnClosed(e);
		}
	}
}
