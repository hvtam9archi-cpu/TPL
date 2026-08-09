using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TPL
{
	public partial class PdfEditorApp : Application
	{
		protected override async void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			L10n.Init();

			try
			{
				StartupRequest request = ReadStartupRequest(e.Args);
				PdfEditorWindow window = PdfEditorWindow.Instance;
				MainWindow = window;
				window.Show();
				await window.InitializeAsync(
					request.Files,
					request.DefaultFileName,
					request.DeleteSourcesOnExit);
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"TPL PDF Editor could not start.\n\n" + ex.Message,
					"TPL PDF Editor",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
				Shutdown(1);
			}
		}

		private static StartupRequest ReadStartupRequest(string[] args)
		{
			if (args.Length >= 2 && string.Equals(args[0], "--session", StringComparison.OrdinalIgnoreCase))
			{
				string sessionPath = Path.GetFullPath(args[1]);
				try
				{
					PdfEditorSession session = PdfEditorSession.Read(sessionPath);
					return new StartupRequest(
						session.Files,
						session.DefaultFileName,
						session.DeleteSourcesOnExit);
				}
				finally
				{
					try { File.Delete(sessionPath); }
					catch (IOException ex)
					{
						System.Diagnostics.Debug.WriteLine($"[PdfEditor] Could not delete session '{sessionPath}': {ex.Message}");
					}
					catch (UnauthorizedAccessException ex)
					{
						System.Diagnostics.Debug.WriteLine($"[PdfEditor] Could not delete session '{sessionPath}': {ex.Message}");
					}
				}
			}

			List<string> files = args
				.Where(argument => !string.IsNullOrWhiteSpace(argument))
				.Select(Path.GetFullPath)
				.Where(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
				.ToList();
			string defaultFileName = files.Count == 1
				? Path.GetFileNameWithoutExtension(files[0])
				: string.Empty;
			return new StartupRequest(files, defaultFileName, false);
		}

		private sealed class StartupRequest
		{
			public StartupRequest(
				IReadOnlyList<string> files,
				string defaultFileName,
				bool deleteSourcesOnExit)
			{
				Files = files;
				DefaultFileName = defaultFileName;
				DeleteSourcesOnExit = deleteSourcesOnExit;
			}

			public IReadOnlyList<string> Files { get; }
			public string DefaultFileName { get; }
			public bool DeleteSourcesOnExit { get; }
		}
	}
}
