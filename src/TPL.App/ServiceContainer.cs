using System;
using Microsoft.Extensions.DependencyInjection;

namespace TPL
{
	/// <summary>
	/// Composition Root — DI Container quản lý tập trung toàn bộ Services, ViewModels.
	/// Được khởi tạo trong IExtensionApplication.Initialize().
	/// </summary>
	public static class ServiceContainer
	{
		private static IServiceProvider _provider;

		/// <summary>Trạng thái DI Container đã được khởi tạo hay chưa.</summary>
		public static bool IsInitialized => _provider != null;

		/// <summary>
		/// Đăng ký tất cả dependencies và build ServiceProvider.
		/// Gọi 1 lần duy nhất trong RibbonSetup.Initialize().
		/// </summary>
		public static void Initialize()
		{
			if (_provider != null) return;

			var services = new ServiceCollection();

			// ── Domain Services ──────────────────────────────────────────
			// services.AddSingleton<IFrameSortingService, FrameSortingService>();
			// services.AddSingleton<IPdfPostProcessor, PdfPostProcessor>();

			// ── Infrastructure Services ──────────────────────────────────
			// services.AddSingleton<IDrawingQueryService, AutoCadQueryService>();
			// services.AddSingleton<IPlotService, AutoCadPlotService>();
			// services.AddSingleton<IMarkerService, MarkerService>();
			// services.AddSingleton<ILicenseRepository, LicenseRepository>();
			// services.AddSingleton<ILocalizationService, LocalizationService>();

			// ── Presentation ViewModels ──────────────────────────────────
			// services.AddTransient<MainWindowViewModel>();
			// services.AddTransient<PdfEditorViewModel>();
			// services.AddTransient<LicenseViewModel>();

			_provider = services.BuildServiceProvider();
		}

		/// <summary>Resolve service từ DI Container.</summary>
		public static T Resolve<T>() where T : class
		{
			if (_provider == null)
				throw new InvalidOperationException("[TPL] ServiceContainer chưa được Initialize. Gọi ServiceContainer.Initialize() trong IExtensionApplication.Initialize() trước.");

			return _provider.GetRequiredService<T>();
		}

		/// <summary>Try-resolve service (trả về null nếu không tìm thấy).</summary>
		public static T TryResolve<T>() where T : class
		{
			return _provider?.GetService<T>();
		}
	}
}
