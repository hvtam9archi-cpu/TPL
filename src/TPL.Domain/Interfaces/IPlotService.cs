using System;
using System.Collections.Generic;
using TPL.Domain.Models;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Thực thi PlotEngine để in bản vẽ.
	/// Implementation: AutoCadPlotService (Infrastructure layer).
	/// </summary>
	public interface IPlotService
	{
		/// <summary>In tất cả các frames theo settings.</summary>
		/// <param name="frames">Danh sách frame đã sort.</param>
		/// <param name="settings">Cấu hình in.</param>
		/// <param name="progress">Callback báo tiến trình.</param>
		/// <returns>Kết quả in.</returns>
		PlotResult PlotAll(List<PlotFrame> frames, PlotSettingsData settings, IProgress<PlotProgress> progress);
	}
}
