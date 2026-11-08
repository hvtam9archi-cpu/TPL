using System;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TPL
{
	internal static class PdfOutput
	{
		// Write beside the destination so replacement stays on the same volume.
		internal static void Save(PdfDocument document, string destinationPath)
		{
			string fullPath = Path.GetFullPath(destinationPath);
			string temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath), $".tpl-{Guid.NewGuid():N}.pdf");
			try
			{
				int expectedPageCount = document.PageCount;
				if (expectedPageCount == 0)
					throw new InvalidOperationException("Cannot save an empty PDF.");

				document.Save(temporaryPath);
				using (PdfDocument savedDocument = PdfReader.Open(temporaryPath, PdfDocumentOpenMode.Import))
				{
					if (savedDocument.PageCount != expectedPageCount)
						throw new InvalidDataException("The saved PDF does not contain all expected pages.");
				}

				if (File.Exists(fullPath))
					File.Replace(temporaryPath, fullPath, null);
				else
					File.Move(temporaryPath, fullPath);
			}
			finally
			{
				try
				{
					if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
				}
				catch (IOException ex)
				{
					System.Diagnostics.Trace.WriteLine($"[TPL] Cannot delete temporary PDF '{temporaryPath}': {ex}");
				}
				catch (UnauthorizedAccessException ex)
				{
					System.Diagnostics.Trace.WriteLine($"[TPL] Cannot delete temporary PDF '{temporaryPath}': {ex}");
				}
			}
		}
	}
}
