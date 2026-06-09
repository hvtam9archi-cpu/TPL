using TPL.Domain.Enums;

namespace TPL.Domain.Models
{
	/// <summary>
	/// Đại diện cho một khung in (plot frame) trong bản vẽ.
	/// Pure C# — sử dụng long Handle thay cho ObjectId.
	/// </summary>
	public class PlotFrame
	{
		/// <summary>Handle của entity trong AutoCAD Database (thay cho ObjectId).</summary>
		public long Handle { get; set; }

		/// <summary>Tọa độ Min X của Extents.</summary>
		public double MinX { get; set; }

		/// <summary>Tọa độ Min Y của Extents.</summary>
		public double MinY { get; set; }

		/// <summary>Tọa độ Max X của Extents.</summary>
		public double MaxX { get; set; }

		/// <summary>Tọa độ Max Y của Extents.</summary>
		public double MaxY { get; set; }

		/// <summary>Tên Layout chứa frame này.</summary>
		public string LayoutName { get; set; }

		/// <summary>Thứ tự (dùng cho MarkedOrder sort).</summary>
		public int OrderIndex { get; set; }

		/// <summary>Văn bản marker (nếu có).</summary>
		public string MarkerText { get; set; }

		/// <summary>Chiều rộng frame.</summary>
		public double Width => System.Math.Abs(MaxX - MinX);

		/// <summary>Chiều cao frame.</summary>
		public double Height => System.Math.Abs(MaxY - MinY);

		/// <summary>Lấy tọa độ base point theo loại.</summary>
		public (double X, double Y) GetBasePoint(BasePoint bpType)
		{
			return bpType switch
			{
				BasePoint.BottomLeft => (MinX, MinY),
				BasePoint.BottomRight => (MaxX, MinY),
				BasePoint.TopLeft => (MinX, MaxY),
				BasePoint.TopRight => (MaxX, MaxY),
				_ => (MinX, MinY),
			};
		}
	}
}
