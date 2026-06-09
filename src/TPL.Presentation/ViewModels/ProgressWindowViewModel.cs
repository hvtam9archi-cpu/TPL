using CommunityToolkit.Mvvm.ComponentModel;

namespace TPL.Presentation.ViewModels
{
	/// <summary>
	/// ViewModel cho ProgressWindow.
	/// Hiển thị tiến trình plot: progress bar + labels.
	/// </summary>
	public partial class ProgressWindowViewModel : ObservableObject
	{
		[ObservableProperty]
		private string title = "Exporting PDF...";

		[ObservableProperty]
		private string mainLabel = "Progress: 0 / 0";

		[ObservableProperty]
		private string subLabel = "";

		[ObservableProperty]
		private int currentPage;

		[ObservableProperty]
		private int totalPages = 1;

		[ObservableProperty]
		private double progressPercent;

		/// <summary>Cập nhật tiến trình.</summary>
		public void Update(int current, int total, string main, string sub)
		{
			CurrentPage = current;
			TotalPages = total > 0 ? total : 1;
			MainLabel = main;
			SubLabel = sub;
			ProgressPercent = (double)current / TotalPages * 100;
		}
	}
}
