using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using TPL.Core.Logging;
using TPL.Domain.Enums;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;
// Alias to avoid clash with Autodesk.AutoCAD.PlottingServices.PlotProgress
using DomainPlotProgress = TPL.Domain.Models.PlotProgress;

namespace TPL.Infrastructure.AutoCad
{
	/// <summary>
	/// Thực thi PlotEngine để in bản vẽ AutoCAD.
	/// Trích xuất từ PlotLogic.PlotAll() — KHÔNG bao gồm post-processing (do Domain đảm nhận).
	/// </summary>
	public class AutoCadPlotService : IPlotService
	{
		private const double MinFrameDimension = 0.001;

		public PlotResult PlotAll(List<PlotFrame> frames, PlotSettingsData settings, IProgress<DomainPlotProgress> progress)
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			var result = new PlotResult();

			if (frames.Count == 0)
			{
				ed.WriteMessage("\nNo plot frames found.");
				return result;
			}

			// Pre-filter degenerate frames
			int originalCount = frames.Count;
			frames.RemoveAll(f => f.Width < MinFrameDimension || f.Height < MinFrameDimension);
			int skippedCount = originalCount - frames.Count;
			if (skippedCount > 0)
				ed.WriteMessage($"\n[TPL] WARNING: Skipped {skippedCount} frame(s) with zero area.");
			if (frames.Count == 0) return result;

			result.IsFilePrinter = IsFilePrinter(settings.DeviceName);

			string baseName = settings.BaseFileName;
			if (string.IsNullOrWhiteSpace(baseName)) baseName = "Drawing1";
			string ext = ".plt";
			string outDir = "";

			if (result.IsFilePrinter)
			{
				ext = settings.DeviceName.ToLower().Contains("pdf") ? ".pdf" : ".plt";
				outDir = settings.OutputPath;
				if (!outDir.EndsWith("\\")) outDir += "\\";
				if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
			}

			// Phase 1: pre-collect all layout/plot data inside ONE transaction
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

					try { psv.SetPlotConfigurationName(ps, settings.DeviceName, settings.PaperSize.Replace(" ", "_")); } catch { }

					Extents2d plotExt;
					if (lay.ModelType)
					{
						using ViewTableRecord vtr = ed.GetCurrentView();
						Matrix3d matWCS2DCS = Matrix3d.WorldToPlane(vtr.ViewDirection) *
							Matrix3d.Displacement(Point3d.Origin - vtr.Target) *
							Matrix3d.Rotation(vtr.ViewTwist, vtr.ViewDirection, vtr.Target);

						Point3d p1 = new Point3d(frame.MinX, frame.MinY, 0).TransformBy(matWCS2DCS);
						Point3d p2 = new Point3d(frame.MaxX, frame.MinY, 0).TransformBy(matWCS2DCS);
						Point3d p3 = new Point3d(frame.MaxX, frame.MaxY, 0).TransformBy(matWCS2DCS);
						Point3d p4 = new Point3d(frame.MinX, frame.MaxY, 0).TransformBy(matWCS2DCS);

						double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
						double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
						double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
						double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

						plotExt = new Extents2d(minX, minY, maxX, maxY);
					}
					else
					{
						plotExt = new Extents2d(frame.MinX, frame.MinY, frame.MaxX, frame.MaxY);
					}
					psv.SetPlotWindowArea(ps, plotExt);
					psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
					psv.SetPlotWindowArea(ps, plotExt);

					try { psv.SetCurrentStyleSheet(ps, settings.PlotStyle); } catch { }

					psv.SetUseStandardScale(ps, true);
					psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
					psv.SetPlotCentered(ps, true);

					ps.PrintLineweights = true;
					ps.PlotPlotStyles = true;
					ps.DrawViewportsFirst = true;
					ps.PlotHidden = false;

					double lenX = frame.Width;
					double lenY = frame.Height;
					bool frameIsLandscape = lenX > lenY;
					bool paperIsLandscape = true;
					if (ps.PlotPaperSize.X > 0 && ps.PlotPaperSize.Y > 0)
						paperIsLandscape = ps.PlotPaperSize.X > ps.PlotPaperSize.Y;

					bool targetIsLandscape = frameIsLandscape;
					if (settings.Orientation == PlotOrientation.Portrait) targetIsLandscape = false;
					else if (settings.Orientation == PlotOrientation.Landscape) targetIsLandscape = true;

					PlotRotation rotation = (targetIsLandscape == paperIsLandscape) ? PlotRotation.Degrees000 : PlotRotation.Degrees090;
					psv.SetPlotRotation(ps, rotation);

					plotJobs.Add((frame.LayoutName, layId, lay.ModelType, ps, new Extents3d(
						new Point3d(frame.MinX, frame.MinY, 0),
						new Point3d(frame.MaxX, frame.MaxY, 0))));
				}
				tr.Commit();
			}
			catch (Exception ex)
			{
				ed.WriteMessage($"\nPlot data error: {ex.Message}");
				result.ErrorMessages.Add(ex.Message);
				return result;
			}

			// Phase 2: PlotEngine with NO active transaction
			short bgPlot = (short)Application.GetSystemVariable("BACKGROUNDPLOT");
			Application.SetSystemVariable("BACKGROUNDPLOT", 0);

			try
			{
				int fileCounter = 1;
				result.TotalPages = plotJobs.Count;

				for (int i = 0; i < plotJobs.Count; i++)
				{
					var (LayoutName, LayoutId, ModelType, Ps, Extents) = plotJobs[i];
					try
					{
						string filePath = "";
						string fileName = "";

						if (result.IsFilePrinter)
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

						progress?.Report(new DomainPlotProgress
						{
							CurrentPage = i + 1,
							TotalPages = plotJobs.Count,
							CurrentLabel = $"{i + 1}/{plotJobs.Count}",
							SubLabel = result.IsFilePrinter ? fileName : $"{fileName} → {settings.DeviceName}"
						});

						LayoutManager.Current.CurrentLayout = LayoutName;
						ed.UpdateScreen();

						if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
						{
							ed.WriteMessage($"\n[TPL] Skipped page {i + 1}: PlotEngine busy.");
							result.ErrorCount++;
							continue;
						}

						using var pe = PlotFactory.CreatePublishEngine();
						pe.BeginPlot(null, null);

						var pi = new PlotInfo { Layout = LayoutId, OverrideSettings = Ps };
						var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
						piv.Validate(pi);

						pe.BeginDocument(pi, doc.Name, null, 1, result.IsFilePrinter, result.IsFilePrinter ? filePath : "");
						pe.BeginPage(new PlotPageInfo(), pi, true, null);
						pe.BeginGenerateGraphics(null);
						pe.EndGenerateGraphics(null);
						pe.EndPage(null);
						pe.EndDocument(null);
						pe.EndPlot(null);

						if (result.IsFilePrinter) result.GeneratedFiles.Add(filePath);
					}
					catch (Exception plotEx)
					{
						result.ErrorCount++;
						result.ErrorMessages.Add($"Page {i + 1}: {plotEx.Message}");
						ed.WriteMessage($"\n[TPL] ERROR page {i + 1}: {plotEx.GetType().Name}: {plotEx.Message}");
					}
					finally
					{
						Ps.Dispose();
					}
				}

				if (result.GeneratedFiles.Count > 0)
					result.FinalPath = result.GeneratedFiles[0];
			}
			finally
			{
				Application.SetSystemVariable("BACKGROUNDPLOT", bgPlot);
			}

			return result;
		}

		private static bool IsFilePrinter(string deviceName)
		{
			if (string.IsNullOrEmpty(deviceName)) return true;
			string lower = deviceName.ToLower();
			string[] fileKeywords = { "pdf", "dwf", "dwg", "png", "jpg", "jpeg", "tiff", "svg", "eps", "plt",
				"publish to web", "dwfx", "design review" };
			return fileKeywords.Any(k => lower.Contains(k));
		}
	}
}
