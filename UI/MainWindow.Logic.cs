using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	public partial class MainWindow
	{
		// ── Load Data ──
		private void LoadData()
		{
			foreach (var p in PlotHelper.GetPrinters()) cbPrinters.Items.Add(p);
			foreach (var s in PlotHelper.GetPlotStyles()) cbStyles.Items.Add(s);

			cbOrd1.Items.Add(L10n.T("sort_lr")); cbOrd1.Items.Add(L10n.T("sort_rl"));
			cbOrd1.Items.Add(L10n.T("sort_tb")); cbOrd1.Items.Add(L10n.T("sort_bt"));
			cbOrd1.Items.Add(L10n.T("sort_sel"));
			cbOrd1.SelectedIndex = 0;

			cbOrd2.Items.Add(L10n.T("sort_none")); cbOrd2.Items.Add(L10n.T("sort_lr"));
			cbOrd2.Items.Add(L10n.T("sort_rl")); cbOrd2.Items.Add(L10n.T("sort_tb"));
			cbOrd2.Items.Add(L10n.T("sort_bt"));
			cbOrd2.SelectedIndex = 3;

			cbBase.Items.Add(L10n.T("anchor_bl")); cbBase.Items.Add(L10n.T("anchor_br"));
			cbBase.Items.Add(L10n.T("anchor_tl")); cbBase.Items.Add(L10n.T("anchor_tr"));
			cbBase.SelectedIndex = 0;

			if (Commands.LastSettings != null)
			{
				var ls = Commands.LastSettings;
				SelectComboItem(cbPrinters, ls.DeviceName);
				SelectComboItem(cbPapers, ls.PaperSize);
				SelectComboItem(cbStyles, ls.PlotStyle);
				txtPath.Text = ls.OutputPath;
				if (ls.FrameType == PlotHelper.FrameType.Block) { rbBlockMode.IsChecked = true; txtBlocks.Text = string.Join(", ", ls.FrameNames); }
				else { rbLayerMode.IsChecked = true; txtLayers.Text = string.Join(", ", ls.FrameNames); }
				rbCurrent.IsChecked = ls.SelectionMode == PlotHelper.SelectionMode.CurrentLayout;
				rbSelect.IsChecked = ls.SelectionMode == PlotHelper.SelectionMode.Manual;
				cbOrd1.SelectedIndex = (int)ls.GroupOrder < cbOrd1.Items.Count ? (int)ls.GroupOrder : 0;
				int crossGroupOrder = (int)ls.CrossGroupOrder;
				cbOrd2.SelectedIndex = crossGroupOrder >= (int)PlotHelper.SortOrder.LeftToRight
					&& crossGroupOrder <= (int)PlotHelper.SortOrder.BottomToTop
					? crossGroupOrder + 1
					: 0;
				cbBase.SelectedIndex = (int)ls.SortBasePoint;
				txtFuzz.Text = ls.Fuzz.ToString();
				chkMark.IsChecked = ls.MarkPlotRegions;
				chkMergePdf.IsChecked = ls.MergePdfs;
				chkOpenPdf.IsChecked = ls.OpenPdf;
				chkConvertImage.IsChecked = ls.ConvertToImage;
				chkPdfEditor.IsChecked = ls.PdfEditor;
				if (ls.ImageFormat == "JPG") rbJpg.IsChecked = true; else rbPng.IsChecked = true;
				txtDpi.Text = ls.ImageDpi.ToString();
				txtFileName.Text = ls.BaseFileName;

				if (ls.Orientation == PlotHelper.PlotOrientation.Portrait) rbOrientPortrait.IsChecked = true;
				else if (ls.Orientation == PlotHelper.PlotOrientation.Landscape) rbOrientLandscape.IsChecked = true;
				else rbOrientAuto.IsChecked = true;
			}
			else
			{
				var activeDoc = Application.DocumentManager.MdiActiveDocument;
				txtFileName.Text = activeDoc != null ? Path.GetFileNameWithoutExtension(activeDoc.Name) : "output";
				SelectComboItem(cbPrinters, "AutoCAD PDF (High Quality Print).pc3");
				SelectComboItem(cbStyles, "monochrome.ctb");
				SelectComboItem(cbPapers, "ISO full bleed A3 (420.00 x 297.00 MM)");
				txtPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
				chkMergePdf.IsChecked = true;
				chkOpenPdf.IsChecked = true;
				chkConvertImage.IsChecked = false;
				chkPdfEditor.IsChecked = false;
			}

			if (pnlImgFormat != null)
			{
				pnlImgFormat.IsEnabled = chkConvertImage.IsChecked == true;
			}
		}

		private void SelectComboItem(System.Windows.Controls.ComboBox cb, string value)
		{
			if (string.IsNullOrEmpty(value)) return;
			for (int i = 0; i < cb.Items.Count; i++)
				if (string.Equals(cb.Items[i].ToString(), value, StringComparison.OrdinalIgnoreCase))
				{ cb.SelectedIndex = i; return; }
		}

		// ── Preview ──
		private List<PlotFrame> GetFramesForSettings(Document document, PlotHelper.PlotSettingsData settings)
		{
			if (IsFrameCacheMatch(document.Database, settings))
				return new List<PlotFrame>(_cachedFrames);

			List<PlotFrame> frames = PlotLogic.SelectFrames(settings);
			_cachedFrameDatabase = document.Database;
			_cachedLayoutName = LayoutManager.Current.CurrentLayout;
			_cachedFrameDataRevision = _frameDataRevision;
			_cachedFrameType = settings.FrameType;
			_cachedSelectionMode = settings.SelectionMode;
			_cachedFrameNames = settings.FrameNames?.ToArray() ?? Array.Empty<string>();
			_cachedManualSelectionIds = settings.ManualSelectionIds?.ToArray() ?? Array.Empty<ObjectId>();
			_cachedFrames.Clear();
			_cachedFrames.AddRange(frames);
			return frames;
		}

		private bool IsFrameCacheMatch(Database database, PlotHelper.PlotSettingsData settings)
		{
			if (_cachedFrameDatabase != database ||
				!string.Equals(_cachedLayoutName, LayoutManager.Current.CurrentLayout, StringComparison.OrdinalIgnoreCase) ||
				_cachedFrameDataRevision != _frameDataRevision ||
				_cachedFrameType != settings.FrameType ||
				_cachedSelectionMode != settings.SelectionMode)
			{
				return false;
			}

			IList<string> frameNames = settings.FrameNames;
			if (frameNames == null) frameNames = Array.Empty<string>();
			if (_cachedFrameNames.Length != frameNames.Count) return false;
			for (int i = 0; i < _cachedFrameNames.Length; i++)
			{
				if (!string.Equals(_cachedFrameNames[i], frameNames[i], StringComparison.OrdinalIgnoreCase))
					return false;
			}

			IList<ObjectId> selectionIds = settings.ManualSelectionIds;
			if (selectionIds == null) selectionIds = Array.Empty<ObjectId>();
			if (_cachedManualSelectionIds.Length != selectionIds.Count) return false;
			for (int i = 0; i < _cachedManualSelectionIds.Length; i++)
			{
				if (_cachedManualSelectionIds[i] != selectionIds[i]) return false;
			}

			return true;
		}

		private void InvalidateFrameCache()
		{
			unchecked { _frameDataRevision++; }
		}

		private void ResetFrameCache()
		{
			InvalidateFrameCache();
			_cachedFrameDatabase = null;
			_cachedLayoutName = string.Empty;
			_cachedFrameDataRevision = -1;
			_cachedFrameNames = Array.Empty<string>();
			_cachedManualSelectionIds = Array.Empty<ObjectId>();
			_cachedFrames.Clear();
		}

		private void UpdatePreview()
		{
			try
			{
				Document activDoc = Application.DocumentManager?.MdiActiveDocument;
				if (activDoc == null || activDoc.IsDisposed) return;
				if (_selectionDoc != null && _selectionDoc != activDoc)
				{
					tempManualSelectionIds.Clear(); txtBlocks.Text = ""; txtLayers.Text = "";
					_selectionDoc = null;
					try { ClearTransientMarkers(); } catch { }
					try { ClearPermanentMarkers(); } catch { }
				}
				bool hasTemplate = rbSelect.IsChecked == true
					? tempManualSelectionIds.Count > 0
					: (rbBlockMode.IsChecked == true ? txtBlocks.Text : txtLayers.Text).Trim().Length > 0;
				if (!hasTemplate)
				{
					lblCount.Text = L10n.T("msg_no_frame");
					lblCount.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
					try { ClearTransientMarkers(); } catch { }
					try { ClearPermanentMarkers(); } catch { }
				}
				else
				{
					var sm = BuildCurrentSettings();
					var frames = GetFramesForSettings(activDoc, sm);
					PlotLogic.SortFrames(frames, sm);
					lblCount.Text = rbSelect.IsChecked == true
						? string.Format("{0}: {1}", L10n.T("rb_manual"), tempManualSelectionIds.Count)
						: string.Format("{0}: {1}", L10n.T("header_frame"), frames.Count);
					lblCount.Foreground = frames.Count > 0
						? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
						: new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
					try { DrawMarkersIfNeeded(frames); } catch { }
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[TPL] UpdatePreview error: {ex.Message}");
			}
		}

		// ── Select Block ──
		private void BtnSelectBlock_Click(object sender, RoutedEventArgs e)
		{
			this.Hide();
			try
			{
				Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null || doc.IsDisposed) return;
				Editor ed = doc.Editor;
				var pso = new PromptSelectionOptions { MessageForAdding = L10n.T("msg_sel_block") };
				var filter = new SelectionFilter(new TypedValue[] { new((int)DxfCode.Start, "INSERT") });
				var psr = ed.GetSelection(pso, filter);
				if (psr.Status == PromptStatus.OK)
				{
					tempManualSelectionIds.Clear();
					try { ClearTransientMarkers(); ClearPermanentMarkers(); } catch { }
					var names = new HashSet<string>();
					using (var tr = doc.Database.TransactionManager.StartTransaction())
					{
						foreach (ObjectId id in psr.Value.GetObjectIds())
						{
							var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
							string n = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
							names.Add(n);
						}
						tr.Commit();
					}
					txtBlocks.Text = string.Join(", ", names);
					rbBlockMode.IsChecked = true;
					_selectionDoc = doc;
					UpdatePreview();
				}
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] SelectBlock error: {ex.Message}"); }
			finally { this.Show(); }
		}

		// ── Select Layer ──
		private void BtnSelectLayer_Click(object sender, RoutedEventArgs e)
		{
			this.Hide();
			try
			{
				Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null || doc.IsDisposed) return;
				Editor ed = doc.Editor;
				var pso = new PromptSelectionOptions { MessageForAdding = L10n.T("msg_sel_layer") };
				var filter = new SelectionFilter(new TypedValue[] { new((int)DxfCode.Start, "LWPOLYLINE") });
				var psr = ed.GetSelection(pso, filter);
				if (psr.Status == PromptStatus.OK)
				{
					tempManualSelectionIds.Clear();
					try { ClearTransientMarkers(); ClearPermanentMarkers(); } catch { }
					var names = new HashSet<string>();
					using (var tr = doc.Database.TransactionManager.StartTransaction())
					{
						foreach (ObjectId id in psr.Value.GetObjectIds())
						{
							var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
							names.Add(ent.Layer);
						}
					}
					txtLayers.Text = string.Join(", ", names);
					rbLayerMode.IsChecked = true;
					_selectionDoc = doc;
					UpdatePreview();
				}
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] SelectLayer error: {ex.Message}"); }
			finally { this.Show(); }
		}

		// ── Select Manual ──
		private void BtnDeleteManual_Click(object sender, RoutedEventArgs e)
		{
			tempManualSelectionIds.Clear();
			_selectionDoc = null;
			try { ClearPermanentMarkers(); } catch { }
			UpdatePreview();
		}

		/// <summary>
		/// Prompts for plot frames without AutoCAD's intermediate selection-count feedback.
		/// The DXF filter must include anonymous <c>*U*</c> records so configured dynamic
		/// blocks remain selectable; their effective names can only be verified after selection.
		/// </summary>
		private static PromptSelectionResult SelectFrameTemplates(
			Editor editor,
			PlotHelper.PlotSettingsData settings,
			string message)
		{
			var options = new PromptSelectionOptions { MessageForAdding = message };
			var filter = new SelectionFilter(PlotLogic.GetFilter(settings));

			// Let TPL report the final valid-frame count once. AutoCAD otherwise reports
			// the preliminary anonymous dynamic-block count and a "filtered out" message.
			editor.WriteMessage(message);
			object originalNoMutt = Application.GetSystemVariable("NOMUTT");
			try
			{
				Application.SetSystemVariable("NOMUTT", 1);
				return editor.GetSelection(options, filter);
			}
			finally
			{
				Application.SetSystemVariable("NOMUTT", originalNoMutt);
			}
		}

		private static bool IsConfiguredBlockFrame(
			ObjectId id,
			Transaction tr,
			HashSet<string> frameNames)
		{
			if (id.IsNull || id.IsErased) return false;
			if (tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference block || block.IsErased)
				return false;

			string blockName = block.Name;
			if (block.IsDynamicBlock &&
				tr.GetObject(block.DynamicBlockTableRecord, OpenMode.ForRead, false) is BlockTableRecord definition)
			{
				blockName = definition.Name;
			}

			return frameNames.Contains(blockName);
		}

		private void BtnSelectManual_Click(object sender, RoutedEventArgs e)
		{
			var ds = BuildCurrentSettings();
			if (ds.FrameNames.Count == 0)
			{
				System.Windows.MessageBox.Show(L10n.T("msg_need_sample"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			Document doc = null;
			this.Hide();
			try
			{
				Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
				doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null || doc.IsDisposed) return;
				Editor ed = doc.Editor;
				PromptSelectionResult psr = SelectFrameTemplates(ed, ds, L10n.T("msg_sel_frames"));
				if (psr.Status == PromptStatus.OK)
				{
					tempManualSelectionIds.Clear();
					var frameNames = new HashSet<string>(ds.FrameNames, StringComparer.OrdinalIgnoreCase);
					using var tr = doc.Database.TransactionManager.StartTransaction();
					foreach (ObjectId id in psr.Value.GetObjectIds())
					{
						if (ds.FrameType != PlotHelper.FrameType.Block || IsConfiguredBlockFrame(id, tr, frameNames))
							tempManualSelectionIds.Add(id);
					}
					tr.Commit();

					// Thông báo số lượng thực tế khớp tên block
					ed.WriteMessage($"\n[TPL] Selected {tempManualSelectionIds.Count} valid frame(s).");
				}
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] SelectManual error: {ex.Message}"); }
			finally { this.Show(); }
			rbSelect.IsChecked = true;
			_selectionDoc = doc;
			try { UpdatePreview(); } catch { }
		}

		private void BtnAddManual_Click(object sender, RoutedEventArgs e)
		{
			var ds = BuildCurrentSettings();
			if (ds.FrameNames.Count == 0)
			{
				System.Windows.MessageBox.Show(L10n.T("msg_need_sample"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			Document doc = null;
			this.Hide();
			try
			{
				Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
				doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null || doc.IsDisposed) return;
				Editor ed = doc.Editor;
				PromptSelectionResult psr = SelectFrameTemplates(ed, ds, "Select frames to Add");
				if (psr.Status == PromptStatus.OK)
				{
					int addedCount = 0;
					var existingIds = new HashSet<ObjectId>(tempManualSelectionIds);
					var frameNames = new HashSet<string>(ds.FrameNames, StringComparer.OrdinalIgnoreCase);
					using var tr = doc.Database.TransactionManager.StartTransaction();
					foreach (ObjectId id in psr.Value.GetObjectIds())
					{
						if (!existingIds.Add(id)) continue;

						if (ds.FrameType != PlotHelper.FrameType.Block || IsConfiguredBlockFrame(id, tr, frameNames))
						{
							tempManualSelectionIds.Add(id);
							addedCount++;
						}
						else existingIds.Remove(id);
					}
					tr.Commit();

					ed.WriteMessage($"\n[TPL] Added {addedCount} frame(s). Total: {tempManualSelectionIds.Count}.");
				}
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] AddManual error: {ex.Message}"); }
			finally { this.Show(); }
			rbSelect.IsChecked = true;
			_selectionDoc = doc;
			try { UpdatePreview(); } catch { }
		}

		private void BtnRemoveManual_Click(object sender, RoutedEventArgs e)
		{
			if (tempManualSelectionIds.Count == 0) return;
			var ds = BuildCurrentSettings();
			Document doc = null;
			this.Hide();
			try
			{
				Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
				doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null || doc.IsDisposed) return;
				Editor ed = doc.Editor;
				PromptSelectionResult psr = SelectFrameTemplates(ed, ds, "Select frames to Remove");
				if (psr.Status == PromptStatus.OK)
				{
					var idsToRemove = new HashSet<ObjectId>(psr.Value.GetObjectIds());
					tempManualSelectionIds.RemoveAll(idsToRemove.Contains);
				}
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TPL] RemoveManual error: {ex.Message}"); }
			finally { this.Show(); }
			rbSelect.IsChecked = true;
			_selectionDoc = doc;
			try { UpdatePreview(); } catch { }
		}

		// ── Plot ──
		private void BtnPlot_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				Settings = BuildCurrentSettings();
				if (string.IsNullOrWhiteSpace(Settings.DeviceName) || string.IsNullOrWhiteSpace(Settings.PaperSize))
				{
					System.Windows.MessageBox.Show("Vui lòng chọn máy in và khổ giấy.", L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
				if (PlotHelper.IsFilePrinter(Settings.DeviceName) && string.IsNullOrWhiteSpace(Settings.OutputPath))
				{
					System.Windows.MessageBox.Show("Vui lòng chọn thư mục xuất file.", L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
				if (Settings.Fuzz < 0)
				{
					System.Windows.MessageBox.Show("Sai số sắp xếp không được âm.", L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
				if (Settings.ConvertToImage && (Settings.ImageDpi < 72 || Settings.ImageDpi > 2400))
				{
					System.Windows.MessageBox.Show("DPI ảnh phải nằm trong khoảng 72–2400.", L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
				if (Settings.FrameNames.Count == 0)
				{ System.Windows.MessageBox.Show(L10n.T("msg_no_frame"), L10n.T("err_title"), MessageBoxButton.OK, MessageBoxImage.Error); return; }
				if (Settings.SelectionMode == PlotHelper.SelectionMode.Manual && Settings.ManualSelectionIds.Count == 0)
				{ System.Windows.MessageBox.Show(L10n.T("msg_no_manual"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
				IsPlotConfirmed = true;
				Commands.LastSettings = Settings;
				Document doc = Application.DocumentManager.MdiActiveDocument;
				if (doc == null || doc.IsDisposed) return;
				List<PlotFrame> frames = GetFramesForSettings(doc, Settings);
				if (frames.Count == 0)
				{ System.Windows.MessageBox.Show(L10n.T("msg_no_result"), L10n.T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
				PlotLogic.SortFrames(frames, Settings);
				using DocumentLock docLock = doc.LockDocument();
				PlotLogic.PlotAll(frames, Settings);
			}
			catch (Exception ex)
			{ System.Windows.MessageBox.Show(string.Format(L10n.T("msg_plot_error"), ex.Message), L10n.T("err_title"), MessageBoxButton.OK, MessageBoxImage.Error); }
		}
	}
}
