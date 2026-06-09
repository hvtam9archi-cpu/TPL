using System;
using TPL.Core.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TPL
{
	/// <summary>
	/// Guard wrapper cho tất cả [CommandMethod] entry points.
	/// Bọc logic trong try-catch + TplLogger để crash-proof toàn bộ plugin.
	/// </summary>
	public static class CommandGuard
	{
		/// <summary>
		/// Thực thi action trong try-catch an toàn.
		/// Log lỗi ra file + Command Line.
		/// </summary>
		/// <param name="commandName">Tên command để log (VD: "TPL", "TPL_LICENSE").</param>
		/// <param name="action">Logic cần thực thi.</param>
		public static void Execute(string commandName, Action action)
		{
			try
			{
				action();
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, $"Command [{commandName}]");
				try
				{
					var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
					ed?.WriteMessage($"\n[TPL] Error in {commandName}: {ex.Message}\n");
				}
				catch { }
			}
		}

		/// <summary>
		/// Thực thi action với return value trong try-catch an toàn.
		/// </summary>
		public static T Execute<T>(string commandName, Func<T> func, T fallback = default)
		{
			try
			{
				return func();
			}
			catch (System.Exception ex)
			{
				TplLogger.Error(ex, $"Command [{commandName}]");
				try
				{
					var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
					ed?.WriteMessage($"\n[TPL] Error in {commandName}: {ex.Message}\n");
				}
				catch { }
				return fallback;
			}
		}
	}
}
