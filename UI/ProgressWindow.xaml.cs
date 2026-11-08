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
			// Force rendering update immediately on UI thread
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				pnlProgress.Value = current;
				pnlProgress.Label = label;
				pnlProgress.Percent = pnlProgress.Maximum > 0
					? $"{(int)((double)current / pnlProgress.Maximum * 100)}%"
					: "0%";
				txtSubTitle.Text = subLabel;
			}));

			// Process message pump to avoid window ghosting/freezing
			AllowUIToUpdate();
		}

		public void SetMax(int max)
		{
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				pnlProgress.Maximum = max;
			}));
			AllowUIToUpdate();
		}

		public void SetSubTitle(string subLabel)
		{
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				txtSubTitle.Text = subLabel;
			}));
			AllowUIToUpdate();
		}

		private void AllowUIToUpdate()
		{
			try
			{
				System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
				Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Windows.Threading.DispatcherOperationCallback(ExitFrame), frame);
				System.Windows.Threading.Dispatcher.PushFrame(frame);
			}
			catch { }
		}

		private object ExitFrame(object f)
		{
			((System.Windows.Threading.DispatcherFrame)f).Continue = false;
			return null;
		}
	}
}

