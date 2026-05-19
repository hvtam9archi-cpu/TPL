using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace TPL
{
	public partial class FramesLibraryWindow : Window
	{
		public ObservableCollection<FrameItem> Frames { get; set; }

		public FramesLibraryWindow()
		{
			InitializeComponent();

			// Dummy data for preview
			Frames = new ObservableCollection<FrameItem>
			{
				new FrameItem { Name = "A1_TitleBlock", Type = "Block", Size = "841x594", Orientation = "Landscape" },
				new FrameItem { Name = "A3_TitleBlock", Type = "Block", Size = "420x297", Orientation = "Landscape" },
				new FrameItem { Name = "A4_Portrait", Type = "Polyline", Size = "210x297", Orientation = "Portrait" }
			};

			gridFrames.ItemsSource = Frames;
		}

		private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ButtonState == MouseButtonState.Pressed)
			{
				DragMove();
			}
		}

		private void BtnClose_Click(object sender, RoutedEventArgs e)
		{
			this.Close();
		}
	}

	public class FrameItem
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public string Size { get; set; }
		public string Orientation { get; set; }
	}
}
