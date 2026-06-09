using System;
using System.IO;
using Serilog;

namespace TPL.Core.Logging
{
	/// <summary>
	/// Logger tập trung cho toàn bộ TPL plugin.
	/// Sử dụng Serilog ghi log ra file (rolling daily, giữ 7 ngày).
	/// Khởi tạo 1 lần trong IExtensionApplication.Initialize().
	/// </summary>
	public static class TplLogger
	{
		private static ILogger _logger;
		private static bool _isInitialized;

		/// <summary>Khởi tạo logger. Gọi 1 lần khi plugin load.</summary>
		public static void Initialize()
		{
			if (_isInitialized) return;

			try
			{
				string logDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
					"TPL", "logs");

				if (!Directory.Exists(logDir))
					Directory.CreateDirectory(logDir);

				_logger = new LoggerConfiguration()
					.MinimumLevel.Debug()
					.WriteTo.File(
						Path.Combine(logDir, "tpl-.log"),
						rollingInterval: RollingInterval.Day,
						retainedFileCountLimit: 7,
						outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
					.CreateLogger();

				_isInitialized = true;
				Info("TPL Logger initialized.");
			}
			catch
			{
				// Nếu không tạo được logger (permission, disk full...) — bỏ qua, không crash plugin
				_isInitialized = false;
			}
		}

		/// <summary>Log lỗi kèm Exception.</summary>
		public static void Error(Exception ex, string context)
		{
			_logger?.Error(ex, "[TPL] {Context}", context);
		}

		/// <summary>Log lỗi từ message string.</summary>
		public static void Error(string message)
		{
			_logger?.Error("[TPL] {Message}", message);
		}

		/// <summary>Log thông tin.</summary>
		public static void Info(string message)
		{
			_logger?.Information("[TPL] {Message}", message);
		}

		/// <summary>Log cảnh báo.</summary>
		public static void Warn(string message)
		{
			_logger?.Warning("[TPL] {Message}", message);
		}

		/// <summary>Log debug (chỉ ghi khi MinLevel = Debug).</summary>
		public static void Debug(string message)
		{
			_logger?.Debug("[TPL] {Message}", message);
		}

		/// <summary>Flush log buffer (gọi khi plugin Terminate).</summary>
		public static void Shutdown()
		{
			try
			{
				(_logger as IDisposable)?.Dispose();
			}
			catch { }
		}
	}
}
