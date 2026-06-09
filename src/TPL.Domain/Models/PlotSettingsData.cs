using System.Collections.Generic;
using TPL.Domain.Enums;

namespace TPL.Domain.Models
{
	/// <summary>
	/// DTO chứa toàn bộ cấu hình in từ giao diện.
	/// Pure C# — không chứa bất kỳ AutoCAD types nào (ObjectId, Database...).
	/// </summary>
	public class PlotSettingsData
	{
		public string DeviceName { get; set; }
		public string PaperSize { get; set; }
		public string PlotStyle { get; set; }
		public string OutputPath { get; set; }
		public string BaseFileName { get; set; }
		public FrameType FrameType { get; set; }
		public List<string> FrameNames { get; set; } = new List<string>();
		public SelectionMode SelectionMode { get; set; }
		public SortOrder GroupOrder { get; set; }
		public SortOrder CrossGroupOrder { get; set; }
		public BasePoint SortBasePoint { get; set; }
		public PlotOrientation Orientation { get; set; }
		public double Fuzz { get; set; }
		public bool MarkPlotRegions { get; set; }
		public bool MergePdfs { get; set; }
		public bool OpenPdf { get; set; }
		public bool ConvertToImage { get; set; }
		public bool PdfEditor { get; set; }
		public string ImageFormat { get; set; } = "PNG";
		public int ImageDpi { get; set; } = 600;

		/// <summary>
		/// Danh sách Handle (long) của các entity được chọn thủ công.
		/// Infrastructure layer sẽ convert ObjectId ↔ Handle khi cần.
		/// </summary>
		public List<long> ManualSelectionHandles { get; set; } = new List<long>();
	}
}
