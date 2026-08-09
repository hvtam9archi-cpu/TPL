using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TPL
{
	internal sealed class PdfEditorSession
	{
		private const string Header = "TPL_PDF_EDITOR_SESSION_V1";
		private const string DefaultNamePrefix = "defaultName=";
		private const string DeleteSourcesPrefix = "deleteSources=";
		private const string FilePrefix = "file=";

		public string DefaultFileName { get; private set; } = string.Empty;
		public bool DeleteSourcesOnExit { get; private set; }
		public IReadOnlyList<string> Files { get; private set; } = Array.Empty<string>();

		public static string Write(
			IEnumerable<string> files,
			string defaultFileName,
			bool deleteSourcesOnExit)
		{
			if (files == null) throw new ArgumentNullException(nameof(files));

			var validFiles = new List<string>();
			foreach (string file in files)
			{
				if (string.IsNullOrWhiteSpace(file)) continue;
				string fullPath = Path.GetFullPath(file);
				if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
					throw new ArgumentException($"Only PDF files can be transferred to the editor: '{fullPath}'.", nameof(files));
				validFiles.Add(fullPath);
			}

			if (validFiles.Count == 0)
				throw new ArgumentException("At least one PDF file is required.", nameof(files));

			string sessionDirectory = Path.Combine(Path.GetTempPath(), "TPL", "PdfEditor");
			Directory.CreateDirectory(sessionDirectory);
			string sessionPath = Path.Combine(sessionDirectory, $"{Guid.NewGuid():N}.tplpdfsession");

			var lines = new List<string>
			{
				Header,
				DeleteSourcesPrefix + (deleteSourcesOnExit ? "1" : "0"),
				DefaultNamePrefix + Encode(defaultFileName ?? string.Empty)
			};
			foreach (string file in validFiles)
				lines.Add(FilePrefix + Encode(file));

			File.WriteAllLines(sessionPath, lines, new UTF8Encoding(false));
			return sessionPath;
		}

		public static PdfEditorSession Read(string sessionPath)
		{
			if (string.IsNullOrWhiteSpace(sessionPath))
				throw new ArgumentException("Session path is required.", nameof(sessionPath));

			string[] lines = File.ReadAllLines(sessionPath, Encoding.UTF8);
			if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
				throw new InvalidDataException("Unsupported PDF editor session format.");

			var files = new List<string>();
			string defaultFileName = string.Empty;
			bool deleteSourcesOnExit = false;
			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line.StartsWith(DefaultNamePrefix, StringComparison.Ordinal))
				{
					defaultFileName = Decode(line.Substring(DefaultNamePrefix.Length));
				}
				else if (line.StartsWith(DeleteSourcesPrefix, StringComparison.Ordinal))
				{
					deleteSourcesOnExit = string.Equals(
						line.Substring(DeleteSourcesPrefix.Length),
						"1",
						StringComparison.Ordinal);
				}
				else if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
				{
					string file = Decode(line.Substring(FilePrefix.Length));
					if (!string.IsNullOrWhiteSpace(file))
					{
						string fullPath = Path.GetFullPath(file);
						if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
							throw new InvalidDataException($"The PDF editor session contains a non-PDF path: '{fullPath}'.");
						files.Add(fullPath);
					}
				}
			}

			if (files.Count == 0)
				throw new InvalidDataException("The PDF editor session contains no files.");

			return new PdfEditorSession
			{
				DefaultFileName = defaultFileName,
				DeleteSourcesOnExit = deleteSourcesOnExit,
				Files = files
			};
		}

		private static string Encode(string value) =>
			Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

		private static string Decode(string value)
		{
			try
			{
				return Encoding.UTF8.GetString(Convert.FromBase64String(value));
			}
			catch (FormatException ex)
			{
				throw new InvalidDataException("The PDF editor session contains invalid data.", ex);
			}
		}
	}
}
