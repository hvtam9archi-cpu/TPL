using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TPL.Domain.Enums;
using TPL.Domain.Interfaces;

namespace TPL.Domain.Services
{
	/// <summary>
	/// Xử lý hậu kỳ PDF: xoay orientation, gộp, convert ảnh.
	/// Trích xuất từ PlotLogic.PlotAll() phần post-processing.
	/// Chỉ dùng PdfSharp + PdfiumViewer — không phụ thuộc AutoCAD.
	/// </summary>
	public class PdfPostProcessor : IPdfPostProcessor
	{
		public void ApplyOrientation(List<string> pdfFiles, PlotOrientation orientation)
		{
			if (orientation == PlotOrientation.Auto || pdfFiles == null || pdfFiles.Count == 0)
				return;

			foreach (string pdfFile in pdfFiles)
			{
				if (!pdfFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
				try
				{
					using var docPdf = PdfReader.Open(pdfFile, PdfDocumentOpenMode.Modify);
					bool modified = false;
					foreach (var page in docPdf.Pages)
					{
						double rawWidth = page.MediaBox.Width;
						double rawHeight = page.MediaBox.Height;

						int currentRotate = page.Rotate;
						bool isRotated = (Math.Abs(currentRotate) / 90) % 2 != 0;

						double visualWidth = isRotated ? rawHeight : rawWidth;
						double visualHeight = isRotated ? rawWidth : rawHeight;

						if (orientation == PlotOrientation.Portrait && visualWidth > visualHeight)
						{
							page.Rotate = (currentRotate + 90) % 360;
							modified = true;
						}
						else if (orientation == PlotOrientation.Landscape && visualHeight > visualWidth)
						{
							page.Rotate = (currentRotate + 270) % 360;
							modified = true;
						}
					}
					if (modified) docPdf.Save(pdfFile);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[TPL] Orientation error on {pdfFile}: {ex.Message}");
				}
			}
		}

		public string MergePdfs(List<string> pdfFiles, string outputPath, string baseName)
		{
			if (pdfFiles == null || pdfFiles.Count <= 1) return pdfFiles?.FirstOrDefault();
			if (!pdfFiles.All(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
				return pdfFiles.FirstOrDefault();

			string mergedPath = Path.Combine(outputPath, $"{baseName}.pdf");
			if (File.Exists(mergedPath)) File.Delete(mergedPath);

			using (var outDoc = new PdfDocument())
			{
				foreach (string f in pdfFiles)
				{
					using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
					for (int p = 0; p < inDoc.PageCount; p++)
						outDoc.AddPage(inDoc.Pages[p]);
				}
				outDoc.Save(mergedPath);
			}

			// Xoá các file gốc sau khi merge
			foreach (string f in pdfFiles)
			{
				try { File.Delete(f); } catch { }
			}

			return mergedPath;
		}

		public List<string> ConvertToImages(List<string> pdfFiles, string imageFormat, int dpi)
		{
			var imageFiles = new List<string>();

			foreach (string pdfFile in pdfFiles)
			{
				string imgExt = imageFormat.ToLower();
				string imgPath = Path.ChangeExtension(pdfFile, imgExt);

				try
				{
					using var docPdf = PdfiumViewer.PdfDocument.Load(pdfFile);
					var size = docPdf.PageSizes[0];
					int width = (int)(size.Width * dpi / 72.0);
					int height = (int)(size.Height * dpi / 72.0);

					using var image = docPdf.Render(0, width, height, dpi, dpi, PdfiumViewer.PdfRenderFlags.Annotations);
					if (imageFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase))
						image.Save(imgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
					else
						image.Save(imgPath, System.Drawing.Imaging.ImageFormat.Png);

					imageFiles.Add(imgPath);
					try { File.Delete(pdfFile); } catch { }
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[TPL] ConvertToImage error: {ex.Message}");
				}
			}

			return imageFiles;
		}
	}
}
