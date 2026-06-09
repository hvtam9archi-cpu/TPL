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
		private readonly string title = "Exporting PDF...";

		[ObservableProperty]
		private readonly string mainLabel = "Progress: 0 / 0";

		[ObservableProperty]
		private readonly string subLabel = "";

		[ObservableProperty]
		private readonly int currentPage;

		[ObservableProperty]
		private readonly int totalPages = 1;

		[ObservableProperty]

<<<<<<< TODO: Unmerged change from project 'TPL', Before:
		private readonly double progressPercent;

		/// <summary>Cập nhật tiến trình.</summary>
=======
		private readonly double progressPercent;

		/// <summary>Cập nhật tiến trình.</summary>
>>>>>>> After
		private readonly double progressPercent;

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
