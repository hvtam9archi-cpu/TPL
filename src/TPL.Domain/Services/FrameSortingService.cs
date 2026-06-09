using System;
using System.Collections.Generic;
using TPL.Domain.Enums;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;

namespace TPL.Domain.Services
{
	/// <summary>
	/// Sắp xếp PlotFrames theo cấu hình Sort (GroupOrder, CrossGroupOrder, BasePoint, Fuzz).
	/// Thuật toán thuần tuý toán học — không phụ thuộc AutoCAD.
	/// Trích xuất từ PlotLogic.SortFrames() gốc.
	/// </summary>
	public class FrameSortingService : IFrameSortingService
	{
		public void SortFrames(List<PlotFrame> frames, PlotSettingsData settings)
		{
			if (frames == null || frames.Count == 0) return;

			if (settings.GroupOrder == SortOrder.SelectionOrder || settings.GroupOrder == SortOrder.None)
				return;

			if (settings.GroupOrder == SortOrder.MarkedOrder)
				return;

			var ord1 = settings.GroupOrder;
			var ord2 = settings.CrossGroupOrder;
			var bp = settings.SortBasePoint;
			double fuzz = settings.Fuzz;

			frames.Sort((f1, f2) =>
			{
				var (p1X, p1Y) = f1.GetBasePoint(bp);
				var (p2X, p2Y) = f2.GetBasePoint(bp);

				if (ord2 != SortOrder.None)
				{
					if (ord1 == SortOrder.LeftToRight || ord1 == SortOrder.RightToLeft)
					{
						if (Math.Abs(p1Y - p2Y) <= fuzz)
							return (ord1 == SortOrder.LeftToRight) ? p1X.CompareTo(p2X) : p2X.CompareTo(p1X);
						return (ord2 == SortOrder.TopToBottom) ? p2Y.CompareTo(p1Y) : p1Y.CompareTo(p2Y);
					}
					else
					{
						if (Math.Abs(p1X - p2X) <= fuzz)
							return (ord1 == SortOrder.TopToBottom) ? p2Y.CompareTo(p1Y) : p1Y.CompareTo(p2Y);
						return (ord2 == SortOrder.LeftToRight) ? p1X.CompareTo(p2X) : p2X.CompareTo(p1X);
					}
				}
				else
				{
					if (ord1 == SortOrder.TopToBottom) return p2Y.CompareTo(p1Y);
					if (ord1 == SortOrder.BottomToTop) return p1Y.CompareTo(p2Y);
					if (ord1 == SortOrder.LeftToRight) return p1X.CompareTo(p2X);
					if (ord1 == SortOrder.RightToLeft) return p2X.CompareTo(p1X);
					return 0;
				}
			});
		}
	}
}
