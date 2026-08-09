using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PdfiumViewer;

namespace TPL
{
	public partial class PdfToolWorkspace : UserControl, IDisposable
	{
		private readonly ObservableCollection<string> _mergeFiles = new();
		private readonly ObservableCollection<PdfPageOrderItem> _rearrangedPages = new();
		private readonly CancellationTokenSource _lifetimeCancellation = new();
		private readonly PdfViewer _viewPdfViewer;
		private MemoryStream _viewPreviewStream;
		private int _viewPageCount;
		private int _viewPageIndex;
		private int _viewRequestId;
		private int _splitPageCount;
		private int _rotatePageCount;
		private int _removePageCount;
		private int _extractPageCount;
		private string _rearrangeSourceFile;
		private PdfToolAction _activeTool;
		private bool _isBusy;
		private bool _isDisposed;

		public PdfToolWorkspace()
		{
			InitializeComponent();
			lstMergeFiles.ItemsSource = _mergeFiles;
			lstRearrangePages.ItemsSource = _rearrangedPages;

			_viewPdfViewer = new PdfViewer
			{
				ShowToolbar = false,
				ShowBookmarks = false,
				Dock = System.Windows.Forms.DockStyle.Fill,
				BackColor = System.Drawing.Color.FromArgb(0x18, 0x1A, 0x1F)
			};
			_viewPdfViewer.Renderer.BackColor = System.Drawing.Color.FromArgb(0x18, 0x1A, 0x1F);
			_viewPdfViewer.Renderer.MouseWheelMode = MouseWheelMode.Zoom;
			viewPdfHost.Child = _viewPdfViewer;
			UpdateViewerNavigation();
		}

		public event EventHandler BackRequested;

		public bool IsBusy => _isBusy;

		public void OpenTool(PdfToolAction action)
		{
			if (_isDisposed) throw new ObjectDisposedException(nameof(PdfToolWorkspace));
			_activeTool = action;
			HideAllPanels();
			ResetViewer();
			txtToolStatus.Text = "Ready";

			switch (action)
			{
				case PdfToolAction.Merge:
					ShowPanel(mergePanel, "Merge PDF", "Combine multiple PDF files in a controlled page order.");
					break;
				case PdfToolAction.Split:
					ShowPanel(splitPanel, "Split PDF", "Create separate PDF files for individual pages or page ranges.");
					break;
				case PdfToolAction.View:
					ShowPanel(viewPanel, "PDF Viewer", "Read a document with dedicated page navigation and zoom controls.");
					break;
				case PdfToolAction.Rotate:
					ShowPanel(rotatePanel, "Rotate PDF pages", "Rotate all pages or only the page ranges you specify.");
					break;
				case PdfToolAction.Remove:
					ShowPanel(removePanel, "Remove PDF pages", "Remove unwanted pages and save the remaining document to a new file.");
					break;
				case PdfToolAction.Extract:
					ShowPanel(extractPanel, "Extract PDF pages", "Export selected pages to a separate PDF in the requested order.");
					break;
				case PdfToolAction.Rearrange:
					ShowPanel(rearrangePanel, "Rearrange PDF pages", "Build a new document by moving pages into a different order.");
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(action), action, "This tool uses the full PDF editor instead of a specialized workspace.");
			}
		}

		public async Task HandleDroppedFilesAsync(IReadOnlyList<string> files)
		{
			if (files == null || files.Count == 0 || _isBusy) return;
			List<string> pdfFiles = files
				.Where(File.Exists)
				.Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
				.Select(Path.GetFullPath)
				.ToList();
			if (pdfFiles.Count == 0) return;

			if (_activeTool == PdfToolAction.Merge)
			{
				AddMergeFiles(pdfFiles);
				return;
			}

			await LoadSourceForActiveToolAsync(pdfFiles[0]);
		}

		private void HideAllPanels()
		{
			mergePanel.Visibility = Visibility.Collapsed;
			splitPanel.Visibility = Visibility.Collapsed;
			viewPanel.Visibility = Visibility.Collapsed;
			rotatePanel.Visibility = Visibility.Collapsed;
			removePanel.Visibility = Visibility.Collapsed;
			extractPanel.Visibility = Visibility.Collapsed;
			rearrangePanel.Visibility = Visibility.Collapsed;
		}

		private void ShowPanel(UIElement panel, string title, string description)
		{
			panel.Visibility = Visibility.Visible;
			txtToolTitle.Text = title;
			txtToolDescription.Text = description;
		}

		private async Task LoadSourceForActiveToolAsync(string filePath)
		{
			switch (_activeTool)
			{
				case PdfToolAction.Split:
					await LoadSplitSourceAsync(filePath);
					break;
				case PdfToolAction.View:
					await LoadViewerSourceAsync(filePath);
					break;
				case PdfToolAction.Rotate:
					await LoadRotateSourceAsync(filePath);
					break;
				case PdfToolAction.Remove:
					await LoadRemoveSourceAsync(filePath);
					break;
				case PdfToolAction.Extract:
					await LoadExtractSourceAsync(filePath);
					break;
				case PdfToolAction.Rearrange:
					await LoadRearrangeSourceAsync(filePath);
					break;
			}
		}

		private void Back_Click(object sender, RoutedEventArgs e)
		{
			RunUiAction(() =>
			{
				if (_isBusy) return;
				ResetViewer();
				BackRequested?.Invoke(this, EventArgs.Empty);
			});
		}

		private async void MergeAdd_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				IReadOnlyList<string> files = PickPdfFiles(true, "Select PDF files to merge");
				if (files.Count == 0) return;
				AddMergeFiles(files);
				await Task.CompletedTask;
			});

		private void AddMergeFiles(IEnumerable<string> files)
		{
			foreach (string file in files)
			{
				string fullPath = Path.GetFullPath(file);
				if (!_mergeFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
					_mergeFiles.Add(fullPath);
			}

			if (_mergeFiles.Count > 0 && string.IsNullOrWhiteSpace(txtMergeOutput.Text))
				txtMergeOutput.Text = SuggestOutputPath(_mergeFiles[0], "_merged");
			txtToolStatus.Text = $"{_mergeFiles.Count} PDF file(s) in the merge queue.";
		}

		private void MergeRemove_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() =>
			{
				List<string> selected = lstMergeFiles.SelectedItems.Cast<string>().ToList();
				foreach (string file in selected) _mergeFiles.Remove(file);
				txtToolStatus.Text = $"{_mergeFiles.Count} PDF file(s) in the merge queue.";
			});

		private void MergeUp_Click(object sender, RoutedEventArgs e) => RunUiAction(() => MoveMergeFile(-1));
		private void MergeDown_Click(object sender, RoutedEventArgs e) => RunUiAction(() => MoveMergeFile(1));

		private void MoveMergeFile(int direction)
		{
			int oldIndex = lstMergeFiles.SelectedIndex;
			int newIndex = oldIndex + direction;
			if (oldIndex < 0 || newIndex < 0 || newIndex >= _mergeFiles.Count) return;
			_mergeFiles.Move(oldIndex, newIndex);
			lstMergeFiles.SelectedIndex = newIndex;
		}

		private void MergeOutput_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() =>
			{
				string source = _mergeFiles.FirstOrDefault();
				string selected = PickOutputPdf(source, "_merged", "Save merged PDF");
				if (!string.IsNullOrEmpty(selected)) txtMergeOutput.Text = selected;
			});

		private async void MergeRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(MergeFilesAsync);

		private async Task MergeFilesAsync()
		{
			if (_mergeFiles.Count == 0) throw new InvalidOperationException("Add at least one PDF file.");
			string outputPath = RequireOutputPath(txtMergeOutput.Text);
			List<string> sourceFiles = _mergeFiles.ToList();
			List<PdfPageReference> pages = await Task.Run(() =>
			{
				var results = new List<PdfPageReference>();
				foreach (string file in sourceFiles)
				{
					int pageCount = PdfDocumentService.GetPageCount(file);
					for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
						results.Add(new PdfPageReference(file, pageIndex, 0));
				}
				return results;
			}, _lifetimeCancellation.Token);
			await SavePagesAsync(pages, outputPath, "Merging PDF files");
		}

		private async void SplitInput_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				string file = PickPdfFiles(false, "Open PDF to split").FirstOrDefault();
				if (!string.IsNullOrEmpty(file)) await LoadSplitSourceAsync(file);
			});

		private async Task LoadSplitSourceAsync(string filePath)
		{
			_splitPageCount = await ReadPageCountAsync(filePath, "Reading source PDF");
			txtSplitInput.Text = filePath;
			txtSplitPageCount.Text = $"{_splitPageCount} pages";
			txtSplitOutputFolder.Text = Path.GetDirectoryName(filePath) ?? string.Empty;
		}

		private void SplitOutput_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() =>
			{
				string folder = PickOutputFolder(txtSplitOutputFolder.Text, "Select split output folder");
				if (!string.IsNullOrEmpty(folder)) txtSplitOutputFolder.Text = folder;
			});

		private async void SplitRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(SplitPdfAsync);

		private async Task SplitPdfAsync()
		{
			string sourceFile = RequireSourceFile(txtSplitInput.Text);
			if (_splitPageCount <= 0) throw new InvalidOperationException("Open a source PDF first.");
			string outputFolder = RequireOutputFolder(txtSplitOutputFolder.Text);
			List<List<int>> groups = rbSplitEveryPage.IsChecked == true
				? Enumerable.Range(0, _splitPageCount).Select(index => new List<int> { index }).ToList()
				: PdfPageRangeParser.ParseGroups(txtSplitRanges.Text, _splitPageCount);

			SetBusy(true, "Splitting PDF");
			try
			{
				string baseName = Path.GetFileNameWithoutExtension(sourceFile);
				await Task.Run(() =>
				{
					for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
					{
						_lifetimeCancellation.Token.ThrowIfCancellationRequested();
						List<PdfPageReference> pages = groups[groupIndex]
							.Select(index => new PdfPageReference(sourceFile, index, 0))
							.ToList();
						string outputPath = Path.Combine(outputFolder, $"{baseName}_part_{groupIndex + 1:000}.pdf");
						PdfDocumentService.Save(pages, outputPath, null, _lifetimeCancellation.Token);
						ReportProgress(groupIndex + 1, groups.Count, "Splitting PDF");
					}
				}, _lifetimeCancellation.Token);
				txtToolStatus.Text = $"Created {groups.Count} PDF file(s) in {outputFolder}";
			}
			finally
			{
				SetBusy(false, null);
			}
		}

		private async void ViewInput_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				string file = PickPdfFiles(false, "Open PDF to view").FirstOrDefault();
				if (!string.IsNullOrEmpty(file)) await LoadViewerSourceAsync(file);
			});

		private async Task LoadViewerSourceAsync(string filePath)
		{
			_viewPageCount = await ReadPageCountAsync(filePath, "Opening PDF viewer");
			txtViewInput.Text = filePath;
			_viewPageIndex = 0;
			await LoadViewerPageAsync();
		}

		private async Task LoadViewerPageAsync()
		{
			if (_viewPageCount <= 0 || string.IsNullOrWhiteSpace(txtViewInput.Text))
			{
				ResetViewer();
				return;
			}

			int requestId = ++_viewRequestId;
			string sourceFile = txtViewInput.Text;
			int pageIndex = _viewPageIndex;
			txtToolStatus.Text = $"Rendering page {pageIndex + 1}...";
			byte[] previewData = await Task.Run(
				() => PdfDocumentService.BuildPreviewData(new PdfPageReference(sourceFile, pageIndex, 0)),
				_lifetimeCancellation.Token);
			if (requestId != _viewRequestId || _isDisposed) return;

			IPdfDocument oldDocument = _viewPdfViewer.Document;
			MemoryStream oldStream = _viewPreviewStream;
			var newStream = new MemoryStream(previewData, writable: false);
			IPdfDocument newDocument = null;
			try
			{
				newDocument = PdfiumViewer.PdfDocument.Load(newStream);
				_viewPreviewStream = newStream;
				_viewPdfViewer.Document = newDocument;
				newDocument = null;
				viewPdfHost.Visibility = Visibility.Visible;
				txtViewPlaceholder.Visibility = Visibility.Collapsed;
			}
			finally
			{
				newDocument?.Dispose();
				if (_viewPreviewStream != newStream) newStream.Dispose();
				oldDocument?.Dispose();
				oldStream?.Dispose();
			}

			UpdateViewerNavigation();
			txtToolStatus.Text = $"Viewing {Path.GetFileName(sourceFile)}";
		}

		private async void ViewPrevious_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				if (_viewPageIndex <= 0) return;
				_viewPageIndex--;
				await LoadViewerPageAsync();
			});

		private async void ViewNext_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				if (_viewPageIndex >= _viewPageCount - 1) return;
				_viewPageIndex++;
				await LoadViewerPageAsync();
			});

		private void ViewZoomOut_Click(object sender, RoutedEventArgs e) => RunUiAction(() => ChangeViewerZoom(0.8));
		private void ViewZoomIn_Click(object sender, RoutedEventArgs e) => RunUiAction(() => ChangeViewerZoom(1.25));

		private void ChangeViewerZoom(double factor)
		{
			if (_viewPdfViewer.Document == null) return;
			double zoom = Math.Max(0.1, Math.Min(5.0, _viewPdfViewer.Renderer.Zoom * factor));
			_viewPdfViewer.Renderer.Zoom = zoom;
		}

		private void UpdateViewerNavigation()
		{
			btnViewPrevious.IsEnabled = _viewPageCount > 0 && _viewPageIndex > 0;
			btnViewNext.IsEnabled = _viewPageCount > 0 && _viewPageIndex < _viewPageCount - 1;
			txtViewPage.Text = _viewPageCount > 0
				? $"Page {_viewPageIndex + 1} / {_viewPageCount}"
				: "Page 0 / 0";
		}

		private void ResetViewer()
		{
			_viewRequestId++;
			IPdfDocument oldDocument = _viewPdfViewer.Document;
			_viewPdfViewer.Document = null;
			oldDocument?.Dispose();
			_viewPreviewStream?.Dispose();
			_viewPreviewStream = null;
			_viewPageCount = 0;
			_viewPageIndex = 0;
			viewPdfHost.Visibility = Visibility.Collapsed;
			txtViewPlaceholder.Visibility = Visibility.Visible;
			UpdateViewerNavigation();
		}

		private async void RotateInput_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				string file = PickPdfFiles(false, "Open PDF to rotate").FirstOrDefault();
				if (!string.IsNullOrEmpty(file)) await LoadRotateSourceAsync(file);
			});

		private async Task LoadRotateSourceAsync(string filePath)
		{
			_rotatePageCount = await ReadPageCountAsync(filePath, "Reading PDF pages");
			txtRotateInput.Text = filePath;
			txtRotatePageCount.Text = $"{_rotatePageCount} pages";
			txtRotateOutput.Text = SuggestOutputPath(filePath, "_rotated");
		}

		private void RotateOutput_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() => SetOutputFromDialog(txtRotateOutput, txtRotateInput.Text, "_rotated", "Save rotated PDF"));

		private async void RotateLeftRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(() => RotatePdfAsync(-90));

		private async void RotateRightRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(() => RotatePdfAsync(90));

		private async Task RotatePdfAsync(int rotationDelta)
		{
			string sourceFile = RequireSourceFile(txtRotateInput.Text);
			string outputPath = RequireOutputPath(txtRotateOutput.Text);
			HashSet<int> selectedPages = PdfPageRangeParser.ParseSelection(txtRotateRanges.Text, _rotatePageCount, allowBlankAsAll: true).ToHashSet();
			List<PdfPageReference> pages = Enumerable.Range(0, _rotatePageCount)
				.Select(index => new PdfPageReference(sourceFile, index, selectedPages.Contains(index) ? rotationDelta : 0))
				.ToList();
			await SavePagesAsync(pages, outputPath, rotationDelta < 0 ? "Rotating pages left" : "Rotating pages right");
		}

		private async void RemoveInput_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				string file = PickPdfFiles(false, "Open PDF to remove pages").FirstOrDefault();
				if (!string.IsNullOrEmpty(file)) await LoadRemoveSourceAsync(file);
			});

		private async Task LoadRemoveSourceAsync(string filePath)
		{
			_removePageCount = await ReadPageCountAsync(filePath, "Reading PDF pages");
			txtRemoveInput.Text = filePath;
			txtRemovePageCount.Text = $"{_removePageCount} pages";
			txtRemoveOutput.Text = SuggestOutputPath(filePath, "_pages_removed");
		}

		private void RemoveOutput_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() => SetOutputFromDialog(txtRemoveOutput, txtRemoveInput.Text, "_pages_removed", "Save PDF after removing pages"));

		private async void RemoveRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(RemovePagesAsync);

		private async Task RemovePagesAsync()
		{
			string sourceFile = RequireSourceFile(txtRemoveInput.Text);
			string outputPath = RequireOutputPath(txtRemoveOutput.Text);
			HashSet<int> removedPages = PdfPageRangeParser.ParseSelection(txtRemoveRanges.Text, _removePageCount, allowBlankAsAll: false).ToHashSet();
			List<PdfPageReference> pages = Enumerable.Range(0, _removePageCount)
				.Where(index => !removedPages.Contains(index))
				.Select(index => new PdfPageReference(sourceFile, index, 0))
				.ToList();
			if (pages.Count == 0) throw new InvalidOperationException("At least one page must remain in the output PDF.");
			await SavePagesAsync(pages, outputPath, "Removing pages");
		}

		private async void ExtractInput_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				string file = PickPdfFiles(false, "Open PDF to extract pages").FirstOrDefault();
				if (!string.IsNullOrEmpty(file)) await LoadExtractSourceAsync(file);
			});

		private async Task LoadExtractSourceAsync(string filePath)
		{
			_extractPageCount = await ReadPageCountAsync(filePath, "Reading PDF pages");
			txtExtractInput.Text = filePath;
			txtExtractPageCount.Text = $"{_extractPageCount} pages";
			txtExtractOutput.Text = SuggestOutputPath(filePath, "_extracted");
		}

		private void ExtractOutput_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() => SetOutputFromDialog(txtExtractOutput, txtExtractInput.Text, "_extracted", "Save extracted PDF pages"));

		private async void ExtractRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(ExtractPagesAsync);

		private async Task ExtractPagesAsync()
		{
			string sourceFile = RequireSourceFile(txtExtractInput.Text);
			string outputPath = RequireOutputPath(txtExtractOutput.Text);
			List<int> selectedPages = PdfPageRangeParser.ParseSelection(txtExtractRanges.Text, _extractPageCount, allowBlankAsAll: false);
			List<PdfPageReference> pages = selectedPages
				.Select(index => new PdfPageReference(sourceFile, index, 0))
				.ToList();
			await SavePagesAsync(pages, outputPath, "Extracting pages");
		}

		private async void RearrangeInput_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(async () =>
			{
				string file = PickPdfFiles(false, "Open PDF to rearrange pages").FirstOrDefault();
				if (!string.IsNullOrEmpty(file)) await LoadRearrangeSourceAsync(file);
			});

		private async Task LoadRearrangeSourceAsync(string filePath)
		{
			int pageCount = await ReadPageCountAsync(filePath, "Reading PDF page order");
			_rearrangeSourceFile = filePath;
			_rearrangedPages.Clear();
			for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
				_rearrangedPages.Add(new PdfPageOrderItem(pageIndex));
			txtRearrangeInput.Text = $"{Path.GetFileName(filePath)} - {pageCount} pages";
			txtRearrangeOutput.Text = SuggestOutputPath(filePath, "_rearranged");
			if (_rearrangedPages.Count > 0) lstRearrangePages.SelectedIndex = 0;
		}

		private void RearrangeUp_Click(object sender, RoutedEventArgs e) => RunUiAction(() => MoveRearrangedPage(-1));
		private void RearrangeDown_Click(object sender, RoutedEventArgs e) => RunUiAction(() => MoveRearrangedPage(1));

		private void MoveRearrangedPage(int direction)
		{
			int oldIndex = lstRearrangePages.SelectedIndex;
			int newIndex = oldIndex + direction;
			if (oldIndex < 0 || newIndex < 0 || newIndex >= _rearrangedPages.Count) return;
			_rearrangedPages.Move(oldIndex, newIndex);
			lstRearrangePages.SelectedIndex = newIndex;
		}

		private void RearrangeOutput_Click(object sender, RoutedEventArgs e) =>
			RunUiAction(() => SetOutputFromDialog(txtRearrangeOutput, _rearrangeSourceFile, "_rearranged", "Save rearranged PDF"));

		private async void RearrangeRun_Click(object sender, RoutedEventArgs e) =>
			await RunUiActionAsync(RearrangePagesAsync);

		private async Task RearrangePagesAsync()
		{
			string sourceFile = RequireSourceFile(_rearrangeSourceFile);
			string outputPath = RequireOutputPath(txtRearrangeOutput.Text);
			if (_rearrangedPages.Count == 0) throw new InvalidOperationException("Open a source PDF first.");
			List<PdfPageReference> pages = _rearrangedPages
				.Select(item => new PdfPageReference(sourceFile, item.PageIndex, 0))
				.ToList();
			await SavePagesAsync(pages, outputPath, "Saving new page order");
		}

		private async Task<int> ReadPageCountAsync(string filePath, string status)
		{
			string sourceFile = RequireSourceFile(filePath);
			SetBusy(true, status);
			try
			{
				int pageCount = await Task.Run(
					() => PdfDocumentService.GetPageCount(sourceFile),
					_lifetimeCancellation.Token);
				txtToolStatus.Text = $"Opened {Path.GetFileName(sourceFile)} - {pageCount} pages";
				return pageCount;
			}
			finally
			{
				SetBusy(false, null);
			}
		}

		private async Task SavePagesAsync(IReadOnlyList<PdfPageReference> pages, string outputPath, string status)
		{
			SetBusy(true, status);
			try
			{
				await Task.Run(() => PdfDocumentService.Save(
					pages,
					outputPath,
					(current, total) => ReportProgress(current, total, status),
					_lifetimeCancellation.Token),
					_lifetimeCancellation.Token);
				txtToolStatus.Text = $"Saved: {outputPath}";
			}
			finally
			{
				SetBusy(false, null);
			}
		}

		private void ReportProgress(int current, int total, string status)
		{
			int percent = total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
			Dispatcher.InvokeAsync(() =>
			{
				toolProgress.Value = percent;
				txtToolStatus.Text = $"{status} - {current} / {total}";
			});
		}

		private void SetBusy(bool isBusy, string status)
		{
			_isBusy = isBusy;
			toolContentHost.IsEnabled = !isBusy;
			toolProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
			if (isBusy)
			{
				toolProgress.Value = 0;
				if (!string.IsNullOrWhiteSpace(status)) txtToolStatus.Text = status;
			}
		}

		private static IReadOnlyList<string> PickPdfFiles(bool multiselect, string title)
		{
			var dialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "PDF Files|*.pdf",
				Multiselect = multiselect,
				Title = title
			};
			return dialog.ShowDialog() == true
				? dialog.FileNames.Select(Path.GetFullPath).ToList()
				: Array.Empty<string>();
		}

		private static string PickOutputPdf(string sourceFile, string suffix, string title)
		{
			var dialog = new Microsoft.Win32.SaveFileDialog
			{
				Filter = "PDF File|*.pdf",
				Title = title,
				AddExtension = true,
				DefaultExt = ".pdf",
				FileName = string.IsNullOrWhiteSpace(sourceFile)
					? "document.pdf"
					: Path.GetFileNameWithoutExtension(sourceFile) + suffix + ".pdf"
			};
			if (!string.IsNullOrWhiteSpace(sourceFile))
				dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFile));
			return dialog.ShowDialog() == true ? Path.GetFullPath(dialog.FileName) : null;
		}

		private static string PickOutputFolder(string initialFolder, string description)
		{
			using var dialog = new System.Windows.Forms.FolderBrowserDialog
			{
				Description = description,
				SelectedPath = Directory.Exists(initialFolder) ? initialFolder : string.Empty,
				ShowNewFolderButton = true
			};
			return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
				? Path.GetFullPath(dialog.SelectedPath)
				: null;
		}

		private static void SetOutputFromDialog(TextBox outputTextBox, string sourceFile, string suffix, string title)
		{
			string selected = PickOutputPdf(sourceFile, suffix, title);
			if (!string.IsNullOrEmpty(selected)) outputTextBox.Text = selected;
		}

		private static string SuggestOutputPath(string sourceFile, string suffix)
		{
			if (string.IsNullOrWhiteSpace(sourceFile)) return string.Empty;
			string fullPath = Path.GetFullPath(sourceFile);
			return Path.Combine(
				Path.GetDirectoryName(fullPath) ?? string.Empty,
				Path.GetFileNameWithoutExtension(fullPath) + suffix + ".pdf");
		}

		private static string RequireSourceFile(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath)) throw new InvalidOperationException("Select an input PDF first.");
			string fullPath = Path.GetFullPath(filePath);
			if (!File.Exists(fullPath)) throw new FileNotFoundException("The input PDF does not exist.", fullPath);
			if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("The input file must be a PDF.");
			return fullPath;
		}

		private static string RequireOutputPath(string outputPath)
		{
			if (string.IsNullOrWhiteSpace(outputPath)) throw new InvalidOperationException("Select an output PDF path.");
			string fullPath = Path.GetFullPath(outputPath);
			if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
				fullPath += ".pdf";
			return fullPath;
		}

		private static string RequireOutputFolder(string outputFolder)
		{
			if (string.IsNullOrWhiteSpace(outputFolder)) throw new InvalidOperationException("Select an output folder.");
			string fullPath = Path.GetFullPath(outputFolder);
			Directory.CreateDirectory(fullPath);
			return fullPath;
		}

		private void RunUiAction(Action action)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				ShowToolError(ex);
			}
		}

		private async Task RunUiActionAsync(Func<Task> action)
		{
			try
			{
				await action();
			}
			catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
			{
				txtToolStatus.Text = "Operation cancelled.";
			}
			catch (Exception ex)
			{
				ShowToolError(ex);
			}
		}

		private void ShowToolError(Exception exception)
		{
			txtToolStatus.Text = exception.Message;
			MessageBox.Show(
				exception.Message,
				txtToolTitle.Text,
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}

		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			_lifetimeCancellation.Cancel();
			ResetViewer();
			_viewPdfViewer.Dispose();
			_lifetimeCancellation.Dispose();
		}
	}

	public sealed class PdfPageOrderItem
	{
		public PdfPageOrderItem(int pageIndex)
		{
			PageIndex = pageIndex;
		}

		public int PageIndex { get; }

		public override string ToString() => $"Page {PageIndex + 1}";
	}
}
