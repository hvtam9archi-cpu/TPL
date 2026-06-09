using System.Collections.Generic;
using TPL.Domain.Models;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Vẽ/xoá markers (transient + permanent) trên bản vẽ.
	/// Implementation: MarkerService (Infrastructure layer).
	/// </summary>
	public interface IMarkerService
	{
		/// <summary>Vẽ transient markers (tạm thời, chỉ hiển thị, không lưu vào DB).</summary>
		void DrawTransientMarkers(List<PlotFrame> frames);

		/// <summary>Vẽ permanent markers (lưu vào layer TPL_MARKERS).</summary>
		void DrawPermanentMarkers(List<PlotFrame> frames);

		/// <summary>Xoá tất cả transient markers.</summary>
		void ClearTransientMarkers();

		/// <summary>Xoá tất cả permanent markers trên layout hiện tại.</summary>
		void ClearPermanentMarkers();

		/// <summary>Xoá permanent markers trên tất cả documents.</summary>
		void ClearAllGlobally();
	}
}
