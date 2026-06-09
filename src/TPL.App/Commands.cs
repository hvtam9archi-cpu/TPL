using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using TPL.Domain.Interfaces;
using TPL.Presentation.ViewModels;
using TPL.Presentation.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	/// <summary>
	/// Entry point commands — thin wrappers chỉ chứa [CommandMethod].
	/// Tất cả logic delegate qua DI → ViewModel → View.
	/// Sử dụng CommandGuard cho crash-proof.
	/// </summary>
	public class Commands
	{
		private static System.Windows.Window _mainWindow;

		[CommandMethod("TPL")]
		public void AutoPlotCommand()
		{
			CommandGuard.Execute("TPL", () =>
			{
				if (!ServiceContainer.IsInitialized) ServiceContainer.Initialize();

				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null) return;

				// License check
				var licenseRepo = ServiceContainer.Resolve<ILicenseRepository>();
				var license = licenseRepo.GetLicenseInfo();
				if (!license.IsValid)
				{
					var licVm = ServiceContainer.Resolve<LicenseWindowViewModel>();
					var licWin = new LicenseWindow(licVm);
					if (Application.ShowModalWindow(licWin) != true)
					{
						doc.Editor.WriteMessage("\n[TPL] License is invalid or expired.\n");
						return;
					}
					// Re-check after activation
					license = licenseRepo.GetLicenseInfo();
					if (!license.IsValid) return;
				}

				licenseRepo.UpdateLastRunDate(license);
				licenseRepo.CheckRemoteRevokeAsync();

				if (_mainWindow == null || !_mainWindow.IsLoaded)
				{
					var vm = ServiceContainer.Resolve<MainWindowViewModel>();
					var mainWin = new MainWindow(vm);
					_mainWindow = mainWin;

					// ─── Set Owner Handle ───
					try
					{
						var acWin = Application.MainWindow;
						if (acWin != null)
						{
							var helper = new System.Windows.Interop.WindowInteropHelper(mainWin)
							{
								Owner = acWin.Handle
							};
						}
					}
					catch { }

					// ─── Wire Delegates ───
					mainWin.OnSelectBlockRequested = (win) =>
					{
						win.Hide();
						try
						{
							Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
							Document currentDoc = Application.DocumentManager.MdiActiveDocument;
							if (currentDoc == null || currentDoc.IsDisposed) return;
							Editor ed = currentDoc.Editor;
							var l10n = ServiceContainer.Resolve<ILocalizationService>();
							var pso = new PromptSelectionOptions { MessageForAdding = l10n.Translate("msg_sel_block") };
							var filter = new SelectionFilter(new TypedValue[] { new((int)DxfCode.Start, "INSERT") });
							var psr = ed.GetSelection(pso, filter);
							if (psr.Status == PromptStatus.OK)
							{
								vm.ManualSelectionHandles.Clear();
								var markerService = ServiceContainer.Resolve<IMarkerService>();
								markerService.ClearTransientMarkers();
								markerService.ClearPermanentMarkers();
								var names = new HashSet<string>();
								using (var tr = currentDoc.Database.TransactionManager.StartTransaction())
								{
									foreach (ObjectId id in psr.Value.GetObjectIds())
									{
										var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
										string n = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
										names.Add(n);
									}
									tr.Commit();
								}
								win.SetBlockNames(string.Join(", ", names));
								win.RefreshPreview();
							}
						}
						catch { }
						finally { win.Show(); }
					};

					mainWin.OnSelectLayerRequested = (win) =>
					{
						win.Hide();
						try
						{
							Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
							Document currentDoc = Application.DocumentManager.MdiActiveDocument;
							if (currentDoc == null || currentDoc.IsDisposed) return;
							Editor ed = currentDoc.Editor;
							var l10n = ServiceContainer.Resolve<ILocalizationService>();
							var pso = new PromptSelectionOptions { MessageForAdding = l10n.Translate("msg_sel_layer") };
							var filter = new SelectionFilter(new TypedValue[] { new((int)DxfCode.Start, "LWPOLYLINE") });
							var psr = ed.GetSelection(pso, filter);
							if (psr.Status == PromptStatus.OK)
							{
								vm.ManualSelectionHandles.Clear();
								var markerService = ServiceContainer.Resolve<IMarkerService>();
								markerService.ClearTransientMarkers();
								markerService.ClearPermanentMarkers();
								var names = new HashSet<string>();
								using (var tr = currentDoc.Database.TransactionManager.StartTransaction())
								{
									foreach (ObjectId id in psr.Value.GetObjectIds())
									{
										var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
										names.Add(ent.Layer);
									}
								}
								win.SetLayerNames(string.Join(", ", names));
								win.RefreshPreview();
							}
						}
						catch { }
						finally { win.Show(); }
					};

					mainWin.OnSelectManualRequested = (win) =>
					{
						win.SyncViewModelFromUI();
						var settings = vm.BuildSettings();
						if (settings.FrameNames.Count == 0)
						{
							var l10n = ServiceContainer.Resolve<ILocalizationService>();
							System.Windows.MessageBox.Show(l10n.Translate("msg_need_sample"), l10n.Translate("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
							return;
						}
						win.Hide();
						try
						{
							Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
							Document currentDoc = Application.DocumentManager.MdiActiveDocument;
							if (currentDoc == null || currentDoc.IsDisposed) return;
							Editor ed = currentDoc.Editor;
							var l10n = ServiceContainer.Resolve<ILocalizationService>();
							
							TypedValue[] filterVals = GetAutoCadFilter(settings);
							var filter = new SelectionFilter(filterVals);
							var pso = new PromptSelectionOptions { MessageForAdding = l10n.Translate("msg_sel_frames") };
							var psr = ed.GetSelection(pso, filter);
							if (psr.Status == PromptStatus.OK)
							{
								vm.ManualSelectionHandles.Clear();
								var markerService = ServiceContainer.Resolve<IMarkerService>();
								markerService.ClearTransientMarkers();
								markerService.ClearPermanentMarkers();
								
								using (var tr = currentDoc.Database.TransactionManager.StartTransaction())
								{
									foreach (ObjectId id in psr.Value.GetObjectIds())
									{
										if (settings.FrameType == TPL.Domain.Enums.FrameType.Block)
										{
											var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
											string name = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
											if (settings.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase)))
												vm.ManualSelectionHandles.Add(id.Handle.Value);
										}
										else
										{
											vm.ManualSelectionHandles.Add(id.Handle.Value);
										}
									}
									tr.Commit();
								}
							}
						}
						catch { }
						finally
						{
							win.SetManualMode();
							win.Show();
							win.RefreshPreview();
						}
					};

					mainWin.OnAddManualRequested = (win) =>
					{
						win.SyncViewModelFromUI();
						var settings = vm.BuildSettings();
						if (settings.FrameNames.Count == 0)
						{
							var l10n = ServiceContainer.Resolve<ILocalizationService>();
							System.Windows.MessageBox.Show(l10n.Translate("msg_need_sample"), l10n.Translate("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
							return;
						}
						win.Hide();
						try
						{
							Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
							Document currentDoc = Application.DocumentManager.MdiActiveDocument;
							if (currentDoc == null || currentDoc.IsDisposed) return;
							Editor ed = currentDoc.Editor;
							
							TypedValue[] filterVals = GetAutoCadFilter(settings);
							var filter = new SelectionFilter(filterVals);
							var pso = new PromptSelectionOptions { MessageForAdding = "Select frames to Add" };
							var psr = ed.GetSelection(pso, filter);
							if (psr.Status == PromptStatus.OK)
							{
								using (var tr = currentDoc.Database.TransactionManager.StartTransaction())
								{
									foreach (ObjectId id in psr.Value.GetObjectIds())
									{
										if (vm.ManualSelectionHandles.Contains(id.Handle.Value)) continue;
										
										if (settings.FrameType == TPL.Domain.Enums.FrameType.Block)
										{
											var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
											string name = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
											if (settings.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase)))
												vm.ManualSelectionHandles.Add(id.Handle.Value);
										}
										else
										{
											vm.ManualSelectionHandles.Add(id.Handle.Value);
										}
									}
									tr.Commit();
								}
							}
						}
						catch { }
						finally
						{
							win.SetManualMode();
							win.Show();
							win.RefreshPreview();
						}
					};

					mainWin.OnRemoveManualRequested = (win) =>
					{
						if (vm.ManualSelectionHandles.Count == 0) return;
						win.SyncViewModelFromUI();
						var settings = vm.BuildSettings();
						win.Hide();
						try
						{
							Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
							Document currentDoc = Application.DocumentManager.MdiActiveDocument;
							if (currentDoc == null || currentDoc.IsDisposed) return;
							Editor ed = currentDoc.Editor;
							
							TypedValue[] filterVals = GetAutoCadFilter(settings);
							var filter = new SelectionFilter(filterVals);
							var pso = new PromptSelectionOptions { MessageForAdding = "Select frames to Remove" };
							var psr = ed.GetSelection(pso, filter);
							if (psr.Status == PromptStatus.OK)
							{
								foreach (ObjectId id in psr.Value.GetObjectIds())
								{
									vm.ManualSelectionHandles.Remove(id.Handle.Value);
								}
							}
						}
						catch { }
						finally
						{
							win.SetManualMode();
							win.Show();
							win.RefreshPreview();
						}
					};

					mainWin.OnEditStyleRequested = (win) =>
					{
						win.SyncViewModelFromUI();
						string styleName = vm.SelectedPlotStyle;
						if (string.IsNullOrEmpty(styleName)) return;
						try
						{
							var currentDoc = Application.DocumentManager.MdiActiveDocument;
							if (currentDoc == null) return;
							string styleDir = "";
							using (currentDoc.LockDocument())
							{
								styleDir = System.IO.Path.Combine((string)Application.GetSystemVariable("ROAMABLEROOTPREFIX"), @"Plotters\Plot Styles");
							}
							string path = System.IO.Path.Combine(styleDir, styleName);
							if (!System.IO.File.Exists(path))
							{
								try
								{
									string sharedDir = (string)Application.GetSystemVariable("PLOTSTYLEDIR");
									if (!string.IsNullOrEmpty(sharedDir))
									{
										string altPath = System.IO.Path.Combine(sharedDir, styleName);
										if (System.IO.File.Exists(altPath)) path = altPath;
									}
								}
								catch { }
							}
							string acadDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
							string styExePath = System.IO.Path.Combine(acadDir, "styexe.exe");
							if (!System.IO.File.Exists(path))
							{
								System.Windows.MessageBox.Show(
									$"Plot style file not found:\n\nFile: {styleName}\nSearched in: {styleDir}",
									"TPL — Edit Plot Style", MessageBoxButton.OK, MessageBoxImage.Warning);
								return;
							}
							if (System.IO.File.Exists(styExePath))
							{
								System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
								{
									FileName = styExePath,
									Arguments = "\"" + path + "\"",
									UseShellExecute = false
								});
							}
							else
							{
								System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
								{
									FileName = path,
									UseShellExecute = true
								});
							}
						}
						catch (System.Exception ex)
						{
							System.Windows.MessageBox.Show(
								$"Cannot open Plot Style Editor.\n\nStyle: {styleName}\nError: {ex.GetType().Name}: {ex.Message}",
								"TPL — Edit Plot Style", MessageBoxButton.OK, MessageBoxImage.Error);
						}
					};

					mainWin.OnDeleteMarksRequested = () =>
					{
						var markerService = ServiceContainer.Resolve<IMarkerService>();
						markerService.ClearPermanentMarkers();
					};

					mainWin.OnBackToEditorRequested = () =>
					{
						var editor = PdfEditorWindow.Instance;
						if (editor != null && editor.IsLoaded)
						{
							editor.Show();
							editor.Activate();
							mainWin.Hide();
						}
					};

					mainWin.OnPreviewRequested = (settings) =>
					{
						var queryService = ServiceContainer.Resolve<IDrawingQueryService>();
						var sortingService = ServiceContainer.Resolve<IFrameSortingService>();
						var markerService = ServiceContainer.Resolve<IMarkerService>();

						var frames = queryService.SelectFrames(settings);
						sortingService.SortFrames(frames, settings);

						// Draw preview markers (transient or permanent)
						if (settings.MarkPlotRegions)
						{
							markerService.ClearTransientMarkers();
							markerService.DrawPermanentMarkers(frames);
						}
						else
						{
							markerService.ClearPermanentMarkers();
							markerService.DrawTransientMarkers(frames);
						}
						return frames;
					};

					mainWin.OnPlotRequested = (win) =>
					{
						win.SyncViewModelFromUI();
						vm.StartPlot();
					};

					vm.OnPdfEditorRequested = (files, baseName) =>
					{
						var editor = PdfEditorWindow.Instance;
						var l10n = ServiceContainer.Resolve<ILocalizationService>();

						editor.Localize = key => l10n.Translate(key);
						editor.SetDefaultFileName(baseName);
						editor.AddPdfFiles(files);

						editor.OnPlusPlotRequested = () =>
						{
							if (_mainWindow is MainWindow mWin)
							{
								mWin.SetSubPlotMode(true);
								mWin.Show();
								mWin.Activate();
							}
						};

						editor.OnCloseReturnToMain = () =>
						{
							if (_mainWindow is MainWindow mWin && mWin.IsLoaded)
							{
								mWin.SetSubPlotMode(false);
								mWin.Show();
								mWin.Activate();
							}
						};

						mainWin.Hide();

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
					};

					_mainWindow.Show();
				}
				else
				{
					_mainWindow.Activate();
				}
			});
		}

		[CommandMethod("TPL_LICENSE")]
		public void LicenseCommand()
		{
			CommandGuard.Execute("TPL_LICENSE", () =>
			{
				if (!ServiceContainer.IsInitialized) ServiceContainer.Initialize();

				var vm = ServiceContainer.Resolve<LicenseWindowViewModel>();
				var licenseWin = new LicenseWindow(vm);
				Application.ShowModalWindow(licenseWin);
			});
		}

		private static TypedValue[] GetAutoCadFilter(TPL.Domain.Models.PlotSettingsData settings)
		{
			if (settings.FrameType == TPL.Domain.Enums.FrameType.Block)
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
	}
}
