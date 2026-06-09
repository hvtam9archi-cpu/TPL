using System.Collections.Generic;
using TPL.Domain.Models;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Sắp xếp frames theo cấu hình.
	/// Implementation: FrameSortingService (Domain layer — pure math).
	/// </summary>
	public interface IFrameSortingService
	{
		/// <summary>Sắp xếp danh sách frames tại chỗ (in-place sort).</summary>
		void SortFrames(List<PlotFrame> frames, PlotSettingsData settings);
	}
}
