using System;
using System.Collections.Generic;
using TPL.Domain.Models;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Xử lý hậu kỳ PDF: merge, rotate orientation, convert to image.
	/// Implementation: PdfPostProcessor (Domain layer — dùng PdfSharp + PdfiumViewer).
	/// </summary>
	public interface IPdfPostProcessor
	{
		/// <summary>Xoay hướng các file PDF theo orientation setting.</summary>
		void ApplyOrientation(List<string> pdfFiles, Enums.PlotOrientation orientation);

		/// <summary>Gộp nhiều file PDF thành 1 file.</summary>
		/// <returns>Đường dẫn file đã merge.</returns>
		string MergePdfs(List<string> pdfFiles, string outputPath, string baseName);

		/// <summary>Convert PDF sang ảnh (PNG/JPG).</summary>
		/// <returns>Danh sách file ảnh đã tạo.</returns>
		List<string> ConvertToImages(List<string> pdfFiles, string imageFormat, int dpi);
	}
}
