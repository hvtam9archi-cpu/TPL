using TPL.Domain.Enums;

namespace TPL.Domain.Interfaces
{
	/// <summary>
	/// Dịch vụ đa ngôn ngữ (Localization).
	/// Implementation: LocalizationService (Infrastructure layer).
	/// </summary>
	public interface ILocalizationService
	{
		/// <summary>Dịch key sang ngôn ngữ hiện tại.</summary>
		string Translate(string key);

		/// <summary>Đặt ngôn ngữ.</summary>
		void SetLanguage(Language language);

		/// <summary>Ngôn ngữ hiện tại.</summary>
		Language CurrentLanguage { get; }

		/// <summary>Khởi tạo ngôn ngữ theo culture hệ thống.</summary>
		void Initialize();
	}
}
