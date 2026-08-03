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

			Application.DocumentManager.DocumentActivated += GlobalDocumentActivated;
			Application.DocumentManager.DocumentToBeDestroyed += GlobalDocumentToBeDestroyed;
			_globalEventsSubscribed = true;
		}

		internal static void UnsubscribeGlobalMarkerEvents()
		{
			if (!_globalEventsSubscribed) return;

			Application.DocumentManager.DocumentActivated -= GlobalDocumentActivated;
			Application.DocumentManager.DocumentToBeDestroyed -= GlobalDocumentToBeDestroyed;
			_globalEventsSubscribed = false;
		}

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
			try
			{
				if (e?.Document == null || !_pendingTransients.TryGetValue(e.Document, out var list)) return;
				foreach (var obj in list)
				{
					try { obj?.Dispose(); } catch { }
				}
				_pendingTransients.Remove(e.Document);
			}
			catch { }
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
					using var docLock = currentDoc.LockDocument();
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
					tm.AddTransient(txt, TransientDrawingMode.Main, 128, new IntegerCollection());
					transientObjects.Add(txt);

					var line = new Line(new Point3d(frame.Extents.MinPoint.X, frame.Extents.MaxPoint.Y, 0), new Point3d(frame.Extents.MaxPoint.X, frame.Extents.MinPoint.Y, 0))
					{
						ColorIndex = 1
					};
					tm.AddTransient(line, TransientDrawingMode.Main, 128, new IntegerCollection());
					transientObjects.Add(line);
				}
				doc.Editor.UpdateScreen();
			}
			catch { ClearTransientMarkers(); }
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
							using var docLock = _markerDoc.LockDocument();
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
						using var docLock = currentDoc.LockDocument();
						foreach (var obj in list)
						{ try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { } }
					}
					catch { }
					_pendingTransients.Remove(currentDoc);
				}
			}
			catch { }
		}

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
			QueueTransientsForCleanup();
			ClearTrackedPermanentMarkers();
			try { UnsubscribeDatabaseEvents(); } catch { }
			base.OnClosed(e);
		}
	}
}
