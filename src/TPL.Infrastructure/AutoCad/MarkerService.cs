using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using TPL.Core.Logging;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL.Infrastructure.AutoCad
{
	/// <summary>
	/// Quản lý markers (transient + permanent) trên bản vẽ AutoCAD.
	/// Trích xuất từ MainWindow.Markers.cs.
	/// Tuân thủ nghiêm quy trình: track danh sách → cleanup khi đóng.
	/// </summary>
	public class MarkerService : IMarkerService
	{
		private const string MarkerLayerName = "TPL_MARKERS";

		private readonly List<DBObject> _transientObjects = new();
		private Document _markerDoc;
		private static readonly Dictionary<Document, List<DBObject>> _pendingTransients = new();
		private static bool _globalEventsSubscribed;

		public MarkerService()
		{
			SubscribeGlobalEvents();
		}

		// ═══════════════════════════════════════════════════════════════════
		// PUBLIC — IMarkerService
		// ═══════════════════════════════════════════════════════════════════

		public void DrawTransientMarkers(List<PlotFrame> frames)
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
					double lenX = frame.MaxX - frame.MinX;
					double lenY = frame.MaxY - frame.MinY;
					if (lenX <= 0 || lenY <= 0) continue;

					var txt = new MText
					{
						Contents = "{\\fVerdana|b0|i0|c0|p0;" + (i + 1) + "}",
						TextHeight = Math.Min(lenX, lenY) / 5.0,
						Location = new Point3d((frame.MinX + frame.MaxX) / 2, (frame.MinY + frame.MaxY) / 2, 0),
						Attachment = AttachmentPoint.MiddleCenter,
						ColorIndex = 1
					};
					tm.AddTransient(txt, TransientDrawingMode.Main, 128, new IntegerCollection());
					_transientObjects.Add(txt);

					var line = new Line(
						new Point3d(frame.MinX, frame.MaxY, 0),
						new Point3d(frame.MaxX, frame.MinY, 0))
					{ ColorIndex = 1 };
					tm.AddTransient(line, TransientDrawingMode.Main, 128, new IntegerCollection());
					_transientObjects.Add(line);
				}
				doc.Editor.UpdateScreen();
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, "DrawTransientMarkers");
				ClearTransientMarkers();
			}
		}

		public void DrawPermanentMarkers(List<PlotFrame> frames)
		{
			ClearPermanentMarkers();
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return;
			try
			{
				using (var docLock = doc.LockDocument())
				using (var tr = doc.Database.TransactionManager.StartTransaction())
				{
					// Ensure layer exists
					var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
					if (!lt.Has(MarkerLayerName))
					{
						lt.UpgradeOpen();
						var ltr = new LayerTableRecord
						{
							Name = MarkerLayerName,
							IsPlottable = false,
							Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
								Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1)
						};
						lt.Add(ltr);
						tr.AddNewlyCreatedDBObject(ltr, true);
					}

					string curLayout = LayoutManager.Current.CurrentLayout;
					var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);

					for (int i = 0; i < frames.Count; i++)
					{
						var frame = frames[i];
						if (frame.LayoutName != curLayout) continue;
						double lenX = frame.MaxX - frame.MinX;
						double lenY = frame.MaxY - frame.MinY;
						if (lenX <= 0 || lenY <= 0) continue;

						var line = new Line(
							new Point3d(frame.MinX, frame.MaxY, 0),
							new Point3d(frame.MaxX, frame.MinY, 0))
						{
							Layer = MarkerLayerName,
							ColorIndex = 1
						};
						btr.AppendEntity(line);
						tr.AddNewlyCreatedDBObject(line, true);

						var txt = new MText
						{
							Layer = MarkerLayerName,
							Contents = "{\\fVerdana|b0|i0|c0|p0;" + (i + 1) + "}",
							TextHeight = Math.Min(lenX, lenY) / 5.0,
							Location = new Point3d(
								(frame.MinX + frame.MaxX) / 2,
								(frame.MinY + frame.MaxY) / 2, 0),
							Attachment = AttachmentPoint.MiddleCenter,
							ColorIndex = 1
						};
						btr.AppendEntity(txt);
						tr.AddNewlyCreatedDBObject(txt, true);
					}
					tr.Commit();
				}
				doc.Editor.UpdateScreen();
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, "DrawPermanentMarkers");
			}
		}

		public void ClearTransientMarkers()
		{
			try
			{
				if (_transientObjects.Count == 0) return;
				Document currentDoc = Application.DocumentManager.MdiActiveDocument;

				// If markers belong to a different doc, queue for later cleanup
				if (_markerDoc != null && currentDoc != null && _markerDoc != currentDoc && !_markerDoc.IsDisposed)
				{
					if (!_pendingTransients.ContainsKey(_markerDoc))
						_pendingTransients[_markerDoc] = new List<DBObject>();
					_pendingTransients[_markerDoc].AddRange(_transientObjects);
					_transientObjects.Clear();
					return;
				}

				if (currentDoc != null)
				{
					using var docLock = currentDoc.LockDocument();
					var tm = TransientManager.CurrentTransientManager;
					foreach (var obj in _transientObjects)
					{
						try
						{
							if (obj != null && !obj.IsDisposed)
							{
								tm.EraseTransient(obj, new IntegerCollection());
								obj.Dispose();
							}
						}
						catch { }
					}
				}
				_transientObjects.Clear();
			}
			catch { _transientObjects.Clear(); }
		}

		public void ClearPermanentMarkers()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null || doc.IsDisposed) return;
			try
			{
				using (var docLock = doc.LockDocument())
				using (var tr = doc.Database.TransactionManager.StartTransaction())
				{
					EraseMarkerEntities(tr, doc.Database);
					tr.Commit();
				}
				doc.Editor.UpdateScreen();
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, "ClearPermanentMarkers");
			}
		}

		public void ClearAllGlobally()
		{
			// Clean transients first
			QueueTransientsForCleanup();

			// Clean permanent markers from ALL open documents
			foreach (Document doc in Application.DocumentManager)
			{
				if (doc == null || doc.IsDisposed) continue;
				try
				{
					using (var docLock = doc.LockDocument())
					using (var tr = doc.Database.TransactionManager.StartTransaction())
					{
						EraseMarkerEntities(tr, doc.Database);
						tr.Commit();
					}
					try { doc.Editor.UpdateScreen(); } catch { }
				}
				catch { }
			}
		}

		// ═══════════════════════════════════════════════════════════════════
		// PRIVATE HELPERS
		// ═══════════════════════════════════════════════════════════════════

		private void QueueTransientsForCleanup()
		{
			try
			{
				var tm = TransientManager.CurrentTransientManager;
				Document currentDoc = Application.DocumentManager.MdiActiveDocument;

				if (_transientObjects.Count > 0 && _markerDoc != null && !_markerDoc.IsDisposed)
				{
					if (_markerDoc == currentDoc)
					{
						try
						{
							using var docLock = _markerDoc.LockDocument();
							foreach (var obj in _transientObjects)
							{
								try
								{
									if (obj != null && !obj.IsDisposed)
									{
										tm.EraseTransient(obj, new IntegerCollection());
										obj.Dispose();
									}
								}
								catch { }
							}
						}
						catch { }
					}
					else
					{
						if (!_pendingTransients.ContainsKey(_markerDoc))
							_pendingTransients[_markerDoc] = new List<DBObject>();
						_pendingTransients[_markerDoc].AddRange(_transientObjects);
					}
					_transientObjects.Clear();
				}

				// Also cleanup any pending transients for current doc
				if (currentDoc != null && _pendingTransients.TryGetValue(currentDoc, out var list))
				{
					try
					{
						using var docLock = currentDoc.LockDocument();
						foreach (var obj in list)
						{
							try
							{
								if (obj != null && !obj.IsDisposed)
								{
									tm.EraseTransient(obj, new IntegerCollection());
									obj.Dispose();
								}
							}
							catch { }
						}
					}
					catch { }
					_pendingTransients.Remove(currentDoc);
				}
			}
			catch { }
		}

		private static void EraseMarkerEntities(Transaction tr, Database db)
		{
			var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
			foreach (ObjectId btrId in bt)
			{
				var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
				if (btr.IsLayout)
				{
					foreach (ObjectId entId in btr)
					{
						try
						{
							var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
							if (ent != null && string.Equals(ent.Layer, MarkerLayerName, StringComparison.OrdinalIgnoreCase))
							{
								ent.UpgradeOpen();
								ent.Erase();
							}
						}
						catch { }
					}
				}
			}
		}

		private void SubscribeGlobalEvents()
		{
			if (_globalEventsSubscribed) return;
			try
			{
				Application.DocumentManager.DocumentActivated += GlobalDocumentActivated;
				Application.DocumentManager.DocumentToBeDestroyed += GlobalDocumentToBeDestroyed;
				_globalEventsSubscribed = true;
			}
			catch { }
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
				{
					try
					{
						if (obj != null && !obj.IsDisposed)
						{
							tm.EraseTransient(obj, new IntegerCollection());
							obj.Dispose();
						}
					}
					catch { }
				}
				_pendingTransients.Remove(e.Document);
			}
			catch { }
		}

		private static void GlobalDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
		{
			try { if (e?.Document != null) _pendingTransients.Remove(e.Document); } catch { }
		}
	}
}
