using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TPL
{
	internal sealed class PdfPageReference
	{
		public PdfPageReference(string sourceFile, int pageIndex, int rotationDelta)
		{
			SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
			PageIndex = pageIndex;
			RotationDelta = rotationDelta;
		}

		public string SourceFile { get; }
		public int PageIndex { get; }
		public int RotationDelta { get; }
	}

	internal static class PdfDocumentService
	{
		public static int GetPageCount(string filePath)
		{
			using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
			return document.PageCount;
		}

		public static byte[] BuildPreviewData(PdfPageReference pageReference)
		{
			if (pageReference == null) throw new ArgumentNullException(nameof(pageReference));
			if (pageReference.PageIndex == -1 && pageReference.RotationDelta == 0)
				return File.ReadAllBytes(pageReference.SourceFile);

			using var sourceDocument = PdfReader.Open(pageReference.SourceFile, PdfDocumentOpenMode.Import);
			using var previewDocument = new PdfDocument();
			AppendReference(previewDocument, sourceDocument, pageReference);
			using var output = new MemoryStream();
			previewDocument.Save(output);
			return output.ToArray();
		}

		public static void Save(
			IReadOnlyList<PdfPageReference> pages,
			string outputPath,
			Action<int, int> reportProgress,
			CancellationToken cancellationToken)
		{
			if (pages == null) throw new ArgumentNullException(nameof(pages));
			if (pages.Count == 0) throw new ArgumentException("At least one page is required.", nameof(pages));
			if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

			string fullOutputPath = Path.GetFullPath(outputPath);
			string outputDirectory = Path.GetDirectoryName(fullOutputPath);
			if (string.IsNullOrEmpty(outputDirectory))
				throw new InvalidOperationException("The output directory is invalid.");
			Directory.CreateDirectory(outputDirectory);

			string temporaryOutputPath = Path.Combine(
				outputDirectory,
				$".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");

			var inputDocuments = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
			var remainingSourceUses = pages
				.GroupBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

			try
			{
				using var outputDocument = new PdfDocument();
				try
				{
					for (int i = 0; i < pages.Count; i++)
					{
						cancellationToken.ThrowIfCancellationRequested();
						PdfPageReference pageReference = pages[i];
						if (!inputDocuments.TryGetValue(pageReference.SourceFile, out PdfDocument inputDocument))
						{
							inputDocument = PdfReader.Open(pageReference.SourceFile, PdfDocumentOpenMode.Import);
							inputDocuments.Add(pageReference.SourceFile, inputDocument);
						}

						AppendReference(outputDocument, inputDocument, pageReference);
						if (--remainingSourceUses[pageReference.SourceFile] == 0)
						{
							inputDocument.Dispose();
							inputDocuments.Remove(pageReference.SourceFile);
						}

						reportProgress?.Invoke(i + 1, pages.Count);
					}
				}
				finally
				{
					foreach (PdfDocument inputDocument in inputDocuments.Values)
						inputDocument.Dispose();
					inputDocuments.Clear();
				}

				outputDocument.Save(temporaryOutputPath);
				ReplaceOutputFile(temporaryOutputPath, fullOutputPath);
			}
			finally
			{
				if (File.Exists(temporaryOutputPath))
				{
					try { File.Delete(temporaryOutputPath); }
					catch (IOException ex)
					{
						System.Diagnostics.Debug.WriteLine($"[PdfEditor] Could not remove temporary output '{temporaryOutputPath}': {ex.Message}");
					}
					catch (UnauthorizedAccessException ex)
					{
						System.Diagnostics.Debug.WriteLine($"[PdfEditor] Could not remove temporary output '{temporaryOutputPath}': {ex.Message}");
					}
				}
			}
		}

		private static void AppendReference(
			PdfDocument outputDocument,
			PdfDocument inputDocument,
			PdfPageReference pageReference)
		{
			if (pageReference.PageIndex == -1)
			{
				for (int pageIndex = 0; pageIndex < inputDocument.PageCount; pageIndex++)
					AppendPage(outputDocument, inputDocument.Pages[pageIndex], pageReference.RotationDelta);
				return;
			}

			if (pageReference.PageIndex < 0 || pageReference.PageIndex >= inputDocument.PageCount)
				throw new InvalidDataException($"Page {pageReference.PageIndex + 1} is not available in '{pageReference.SourceFile}'.");

			AppendPage(outputDocument, inputDocument.Pages[pageReference.PageIndex], pageReference.RotationDelta);
		}

		private static void AppendPage(PdfDocument outputDocument, PdfPage sourcePage, int rotationDelta)
		{
			PdfPage page = outputDocument.AddPage(sourcePage);
			int rotation = (page.Rotate + rotationDelta) % 360;
			if (rotation < 0) rotation += 360;
			page.Rotate = rotation;
		}

		private static void ReplaceOutputFile(string temporaryOutputPath, string outputPath)
		{
			if (!File.Exists(outputPath))
			{
				File.Move(temporaryOutputPath, outputPath);
				return;
			}

			try
			{
				File.Replace(temporaryOutputPath, outputPath, null);
			}
			catch (PlatformNotSupportedException)
			{
				File.Copy(temporaryOutputPath, outputPath, true);
				File.Delete(temporaryOutputPath);
			}
			catch (IOException)
			{
				File.Copy(temporaryOutputPath, outputPath, true);
				File.Delete(temporaryOutputPath);
			}
		}
	}
}
