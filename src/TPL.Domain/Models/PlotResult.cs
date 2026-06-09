using System.Collections.Generic;

namespace TPL.Domain.Models
{
	/// <summary>Kết quả của quá trình in.</summary>
	public class PlotResult
	{
		/// <summary>Tổng số trang đã in.</summary>
		public int TotalPages { get; set; }

		/// <summary>Số trang lỗi.</summary>
		public int ErrorCount { get; set; }

		/// <summary>Danh sách file đã tạo (nếu xuất file).</summary>
		public List<string> GeneratedFiles { get; set; } = new List<string>();

		/// <summary>Đường dẫn file cuối cùng (merged hoặc first file).</summary>
		public string FinalPath { get; set; }

		/// <summary>Có phải máy in file (PDF, DWF...) không.</summary>
		public bool IsFilePrinter { get; set; }

		/// <summary>Tin nhắn lỗi (nếu có).</summary>
		public List<string> ErrorMessages { get; set; } = new List<string>();
	}

	/// <summary>Thông tin tiến trình in — dùng cho IProgress&lt;T&gt;.</summary>
	public class PlotProgress
	{
		/// <summary>Trang hiện tại (1-based).</summary>
		public int CurrentPage { get; set; }

		/// <summary>Tổng số trang.</summary>
		public int TotalPages { get; set; }

		/// <summary>Tên file/trang hiện tại.</summary>
		public string CurrentLabel { get; set; }

		/// <summary>Nhãn phụ (tên file đầu ra).</summary>
		public string SubLabel { get; set; }
	}
}
