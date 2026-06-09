using System;
using Microsoft.Extensions.DependencyInjection;
using TPL.Domain.Interfaces;
using TPL.Domain.Services;
using TPL.Infrastructure.AutoCad;
using TPL.Infrastructure.License;
using TPL.Infrastructure.Localization;
using TPL.Presentation.ViewModels;

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

			// ── Domain Services (pure logic, no AutoCAD) ────────────────
			services.AddSingleton<IFrameSortingService, FrameSortingService>();
			services.AddSingleton<IPdfPostProcessor, PdfPostProcessor>();

			// ── Infrastructure Services (AutoCAD + System) ──────────────
			services.AddSingleton<IDrawingQueryService, AutoCadQueryService>();
			services.AddSingleton<IPlotService, AutoCadPlotService>();
			services.AddSingleton<IMarkerService, MarkerService>();
			services.AddSingleton<ILicenseRepository, LicenseRepository>();
			services.AddSingleton<ILocalizationService, LocalizationService>();

			// ── Presentation ViewModels ─────────────────────────────────
			services.AddTransient<MainWindowViewModel>();
			services.AddTransient<LicenseWindowViewModel>();
			services.AddTransient<ProgressWindowViewModel>();

			_provider = services.BuildServiceProvider();

			// Initialize localization immediately
			var l10n = _provider.GetRequiredService<ILocalizationService>();
			l10n.Initialize();
		}

		/// <summary>Resolve service từ DI Container.</summary>
		public static T Resolve<T>() where T : class
		{
			if (_provider == null)
				throw new InvalidOperationException("[TPL] ServiceContainer chưa được Initialize.");

			return _provider.GetRequiredService<T>();
		}

		/// <summary>Try-resolve service (trả về null nếu không tìm thấy).</summary>
		public static T TryResolve<T>() where T : class
		{
			return _provider?.GetService<T>();
		}
	}
}
