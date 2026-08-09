using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace TPL
{
	internal static class PdfEditorLauncher
	{
		private const string EditorExecutableName = "TPL.PdfEditor.exe";

		public static bool TryLaunch(
			IReadOnlyCollection<string> pdfFiles,
			string defaultFileName,
			bool deleteSourcesOnExit,
			out string errorMessage)
		{
			errorMessage = string.Empty;
			if (pdfFiles == null || pdfFiles.Count == 0)
			{
				errorMessage = "No PDF files were generated.";
				return false;
			}

			string sessionPath = null;
			try
			{
				string executablePath = ResolveExecutablePath();
				if (executablePath == null)
				{
					errorMessage = $"{EditorExecutableName} was not found in the TPL bundle.";
					return false;
				}

				sessionPath = PdfEditorSession.Write(pdfFiles, defaultFileName, deleteSourcesOnExit);
				var startInfo = new ProcessStartInfo
				{
					FileName = executablePath,
					Arguments = "--session " + QuoteArgument(sessionPath),
					UseShellExecute = true,
					WorkingDirectory = Path.GetDirectoryName(executablePath)
				};
				using Process process = Process.Start(startInfo);
				if (process == null)
					throw new InvalidOperationException("Windows did not start the PDF editor process.");
				return true;
			}
			catch (Exception ex)
			{
				errorMessage = ex.Message;
				if (!string.IsNullOrEmpty(sessionPath))
				{
					try { File.Delete(sessionPath); }
					catch (IOException cleanupException)
					{
						Debug.WriteLine($"[TPL] Could not delete PDF editor session '{sessionPath}': {cleanupException.Message}");
					}
					catch (UnauthorizedAccessException cleanupException)
					{
						Debug.WriteLine($"[TPL] Could not delete PDF editor session '{sessionPath}': {cleanupException.Message}");
					}
				}
				return false;
			}
		}

		private static string ResolveExecutablePath()
		{
			string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			if (string.IsNullOrEmpty(assemblyDirectory)) return null;

			string[] candidates =
			{
				Path.Combine(assemblyDirectory, "PdfEditor", EditorExecutableName),
				Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "PdfEditor", EditorExecutableName)),
				Path.Combine(assemblyDirectory, EditorExecutableName)
			};
			foreach (string candidate in candidates)
			{
				if (File.Exists(candidate)) return candidate;
			}

			return null;
		}

		private static string QuoteArgument(string value) =>
			"\"" + value.Replace("\"", "\\\"") + "\"";
	}
}
