using System.Collections.Generic;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;

namespace TPL.Infrastructure.AutoCad
{
	/// <summary>
	/// Placeholder MarkerService — sẽ implement đầy đủ khi di chuyển MainWindow.Markers.cs.
	/// Hiện tại cung cấp no-op implementations để compile.
	/// </summary>
	public class MarkerService : IMarkerService
	{
		public void DrawTransientMarkers(List<PlotFrame> frames)
		{
			// TODO: Implement from MainWindow.Markers.cs
		}

		public void DrawPermanentMarkers(List<PlotFrame> frames)
		{
			// TODO: Implement from MainWindow.Markers.cs
		}

		public void ClearTransientMarkers()
		{
			// TODO: Implement
		}

		public void ClearPermanentMarkers()
		{
			// TODO: Implement
		}

		public void ClearAllGlobally()
		{
			// TODO: Implement
		}
	}
}
