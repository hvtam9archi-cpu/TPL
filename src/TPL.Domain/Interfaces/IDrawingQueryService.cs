using System.Collections.Generic;
using TPL.Domain.Models;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Truy vấn dữ liệu bản vẽ AutoCAD (read-only).
	/// Implementation: AutoCadQueryService (Infrastructure layer).
	/// </summary>
	public interface IDrawingQueryService
	{
		/// <summary>Lấy danh sách máy in (Plot Devices).</summary>
		List<string> GetPrinters();

		/// <summary>Lấy danh sách khổ giấy của máy in.</summary>
		List<string> GetPaperSizes(string deviceName);

		/// <summary>Lấy danh sách Plot Styles (CTB/STB).</summary>
		List<string> GetPlotStyles();

		/// <summary>Lấy danh sách Block names (không ẩn, không layout).</summary>
		List<string> GetBlockNames();

		/// <summary>Lấy danh sách Layer names.</summary>
		List<string> GetLayerNames();

		/// <summary>Kiểm tra device có phải máy in file (PDF, DWF...) hay máy in vật lý.</summary>
		bool IsFilePrinter(string deviceName);

		/// <summary>Quét và trả về danh sách PlotFrame theo settings.</summary>
		List<PlotFrame> SelectFrames(PlotSettingsData settings);

		/// <summary>Lấy tên file bản vẽ hiện tại (không extension).</summary>
		string GetCurrentDrawingName();

		/// <summary>Lấy tên layout hiện tại.</summary>
		string GetCurrentLayoutName();
	}
}
