using System;
using System.Windows;

namespace TPL
{
	public partial class ProgressWindow : Window
	{
		public ProgressWindow(string title, int max)
		{
			InitializeComponent();
			txtTitle.Text = title;
			pnlProgress.Maximum = max;
		}

		public void UpdateProgress(int current, string label, string subLabel)
		{
			// Ensure UI updates immediately
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				pnlProgress.Value = current;
				pnlProgress.Label = label;
				pnlProgress.Percent = pnlProgress.Maximum > 0
					? $"{(int)((double)current / pnlProgress.Maximum * 100)}%"
					: "0%";
				txtSubTitle.Text = subLabel;
			}));
		}

		public void SetMax(int max)
		{
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				pnlProgress.Maximum = max;
			}));
		}

		public void SetSubTitle(string subLabel)
		{
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				txtSubTitle.Text = subLabel;
			}));
		}
	}
}
