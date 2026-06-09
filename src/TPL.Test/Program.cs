using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;
using TPL.Domain.Services;
using TPL.Presentation.ViewModels;
using TPL.Presentation.Views;

namespace TPL.Test
{
	// ─── Mock Services ───────────────────────────────────────────────

	public class MockDrawingQueryService : IDrawingQueryService
	{
		public List<string> GetPrinters() => new() { "Mock PDF Printer", "Mock Paper Printer" };
		public List<string> GetPaperSizes(string deviceName) => new() { "A4 (210 x 297 mm)", "A3 (297 x 420 mm)", "A2 (420 x 594 mm)" };
		public List<string> GetPlotStyles() => new() { "monochrome.ctb", "acad.ctb", "grayscale.ctb" };
		public List<string> GetBlockNames() => new() { "FrameBlock", "TitleBlock", "A3_Frame" };
		public List<string> GetLayerNames() => new() { "0", "Defpoints", "TPL_MARKERS", "Khung_In" };
		public bool IsFilePrinter(string deviceName) => true;
		public List<PlotFrame> SelectFrames(PlotSettingsData settings)
		{
			return new List<PlotFrame>
			{
				new PlotFrame { Handle = 1, MinX = 0, MinY = 0, MaxX = 420, MaxY = 297, LayoutName = "Model" },
				new PlotFrame { Handle = 2, MinX = 500, MinY = 0, MaxX = 920, MaxY = 297, LayoutName = "Model" },
				new PlotFrame { Handle = 3, MinX = 1000, MinY = 0, MaxX = 1420, MaxY = 297, LayoutName = "Model" }
			};
		}
		public string GetCurrentDrawingName() => "MockDrawing_Plan";
		public string GetCurrentLayoutName() => "Model";
	}

	public class MockPlotService : IPlotService
	{
		public PlotResult PlotAll(List<PlotFrame> frames, PlotSettingsData settings, IProgress<PlotProgress> progress)
		{
			// Simulate folder structure
			string tempDir = Path.Combine(Path.GetTempPath(), "TplMockPlot");
			if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

			var generated = new List<string>();
			for (int i = 0; i < frames.Count; i++)
			{
				System.Threading.Thread.Sleep(300); // Simulate processing latency
				progress?.Report(new PlotProgress
				{
					CurrentPage = i + 1,
					TotalPages = frames.Count,
					CurrentLabel = $"{i + 1}/{frames.Count}",
					SubLabel = $"Rendering: page_{i + 1:D2}.pdf"
				});

				// Create a simple text file renamed as pdf for test
				string file = Path.Combine(tempDir, $"mock_page_{i + 1:D2}.pdf");
				File.WriteAllText(file, "%PDF-1.4 Mock content here");
				generated.Add(file);
			}

			return new PlotResult
			{
				IsFilePrinter = true,
				TotalPages = frames.Count,
				GeneratedFiles = generated,
				FinalPath = generated.FirstOrDefault()
			};
		}
	}

	public class MockMarkerService : IMarkerService
	{
		public void DrawTransientMarkers(List<PlotFrame> frames) { }
		public void ClearTransientMarkers() { }
		public void DrawPermanentMarkers(List<PlotFrame> frames) { }
		public void ClearPermanentMarkers() { }
		public void ClearAllGlobally() { }
	}

	public class MockLocalizationService : ILocalizationService
	{
		public string Translate(string key) => key; // Passthrough
		public void SetLanguage(TPL.Domain.Enums.Language lang) { }
		public void Initialize() { }
		public TPL.Domain.Enums.Language CurrentLanguage => TPL.Domain.Enums.Language.Vietnamese;
	}

	public class MockLicenseRepository : ILicenseRepository
	{
		public LicenseInfo GetLicenseInfo() => new LicenseInfo { ExpirationDate = DateTime.Now.AddDays(30) };
		public string GetHardwareId() => "MOCK-HWID-KEY-1234567890";
		public bool ActivateLicense(string key, out string message)
		{
			message = "Activation successful!";
			return true;
		}
		public void UpdateLastRunDate(LicenseInfo info) { }
		public void CheckRemoteRevokeAsync() { }
		public void SaveLicenseInfo(LicenseInfo info) { }
		public string GenerateKey(string hwid, int days, string note) => "MOCK-KEY";
	}

	// ─── Entry Point ──────────────────────────────────────────────────

	public class Program
	{
		[STAThread]
		public static void Main()
		{
			var services = new ServiceCollection();

			// Mocks
			services.AddSingleton<IDrawingQueryService, MockDrawingQueryService>();
			services.AddSingleton<IPlotService, MockPlotService>();
			services.AddSingleton<IMarkerService, MockMarkerService>();
			services.AddSingleton<ILocalizationService, MockLocalizationService>();
			services.AddSingleton<ILicenseRepository, MockLicenseRepository>();

			// Real pure domain logic services
			services.AddSingleton<IFrameSortingService, FrameSortingService>();
			services.AddSingleton<IPdfPostProcessor, PdfPostProcessor>();

			// Presentation ViewModels
			services.AddTransient<MainWindowViewModel>();
			services.AddTransient<LicenseWindowViewModel>();
			services.AddTransient<ProgressWindowViewModel>();

			var serviceProvider = services.BuildServiceProvider();

			var app = new Application();
			
			// Load DarkTheme styling resources
			var darkThemeUri = new Uri("pack://application:,,,/TPL.Presentation;component/Resources/DarkTheme.xaml", UriKind.Absolute);
			app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = darkThemeUri });

			var vm = serviceProvider.GetRequiredService<MainWindowViewModel>();
			var mainWin = new MainWindow(vm);

			// Wire callbacks
			mainWin.OnSelectBlockRequested = (win) => { win.SetBlockNames("FrameBlock, TitleBlock"); win.RefreshPreview(); };
			mainWin.OnSelectLayerRequested = (win) => { win.SetLayerNames("TPL_MARKERS, Khung_In"); win.RefreshPreview(); };
			mainWin.OnSelectManualRequested = (win) => { win.SetManualMode(); win.RefreshPreview(); };
			mainWin.OnAddManualRequested = (win) => { win.RefreshPreview(); };
			mainWin.OnRemoveManualRequested = (win) => { win.RefreshPreview(); };
			mainWin.OnEditStyleRequested = (win) => MessageBox.Show("Plot Style Editor Mock Invocation", "TPL Test", MessageBoxButton.OK, MessageBoxImage.Information);
			mainWin.OnDeleteMarksRequested = () => MessageBox.Show("Clear Transient Markers Mock Invocation", "TPL Test", MessageBoxButton.OK, MessageBoxImage.Information);
			
			mainWin.OnPreviewRequested = (settings) =>
			{
				var qs = serviceProvider.GetRequiredService<IDrawingQueryService>();
				return qs.SelectFrames(settings);
			};

			mainWin.OnPlotRequested = (win) =>
			{
				win.SyncViewModelFromUI();
				vm.StartPlot();
			};

			vm.OnPdfEditorRequested = (files, baseName) =>
			{
				var editor = PdfEditorWindow.Instance;
				editor.Localize = key => key;
				editor.SetDefaultFileName(baseName);
				editor.AddPdfFiles(files);

				editor.OnPlusPlotRequested = () =>
				{
					mainWin.SetSubPlotMode(true);
					mainWin.Show();
					mainWin.Activate();
				};

				editor.OnCloseReturnToMain = () =>
				{
					mainWin.SetSubPlotMode(false);
					mainWin.Show();
					mainWin.Activate();
				};

				mainWin.Hide();
				editor.ShowDialog();
			};

			app.Run(mainWin);
		}
	}
}
