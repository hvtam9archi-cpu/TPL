namespace TPL.Domain.Enums
{
	/// <summary>Loại khung in: Block (INSERT) hoặc Polyline (LWPOLYLINE).</summary>
	public enum FrameType
	{
		Block,
		Polyline
	}

	/// <summary>Chế độ chọn khung in.</summary>
	public enum SelectionMode
	{
		AllLayouts,
		CurrentLayout,
		Manual
	}

	/// <summary>Thứ tự sắp xếp khung in.</summary>
	public enum SortOrder
	{
		LeftToRight,
		RightToLeft,
		TopToBottom,
		BottomToTop,
		SelectionOrder,
		MarkedOrder,
		None
	}

	/// <summary>Điểm neo (base point) để tính toán sắp xếp.</summary>
	public enum BasePoint
	{
		BottomLeft,
		BottomRight,
		TopLeft,
		TopRight
	}

	/// <summary>Hướng in (Portrait/Landscape/Auto).</summary>
	public enum PlotOrientation
	{
		Auto,
		Portrait,
		Landscape
	}

	/// <summary>Ngôn ngữ giao diện.</summary>
	public enum Language
	{
		Vietnamese,
		English,
		ChineseSimplified,
		Korean,
		Japanese
	}
}
