using System.Windows.Controls;

namespace TPL
{
	/// <summary>
	/// Giao diện thanh thông báo tiến trình đồng bộ Dark Theme.
	/// Có thể tái sử dụng trên các Window/Panel khác.
	/// </summary>
	public partial class DarkProgressControl : UserControl
	{
		public DarkProgressControl()
		{
			InitializeComponent();
		}

		public double Value
		{
			get => progressBar.Value;
			set => progressBar.Value = value;
		}

		public double Maximum
		{
			get => progressBar.Maximum;
			set => progressBar.Maximum = value;
		}

		public string Label
		{
			get => txtProgressLabel.Text;
			set => txtProgressLabel.Text = value;
		}

		public string Percent
		{
			get => txtProgressPercent.Text;
			set => txtProgressPercent.Text = value;
		}
	}
}
