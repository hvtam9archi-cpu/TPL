using System;
using System.Windows;
using TPL.Presentation.ViewModels;

namespace TPL.Presentation.Views
{
	public partial class ProgressWindow : Window
	{
		private readonly ProgressWindowViewModel _vm;

		public ProgressWindow(ProgressWindowViewModel vm)
		{
			_vm = vm;
			DataContext = _vm;
			InitializeComponent();
		}

		/// <summary>Cập nhật tiến trình — gọi từ bên ngoài (Main thread).</summary>
		public void UpdateProgress(int current, string label, string subLabel)
		{
			Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
			{
				pnlProgress.Value = current;
				pnlProgress.Label = label;
				pnlProgress.Percent = pnlProgress.Maximum > 0
					? $"{(int)((double)current / pnlProgress.Maximum * 100)}%"
					: "0%";
				txtSubTitle.Text = subLabel;
			}));
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

		private void AllowUIToUpdate()
		{
			try
			{
				var frame = new System.Windows.Threading.DispatcherFrame();
				Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
					new System.Windows.Threading.DispatcherOperationCallback(f =>
					{
						((System.Windows.Threading.DispatcherFrame)f).Continue = false;
						return null;
					}), frame);
				System.Windows.Threading.Dispatcher.PushFrame(frame);
			}
			catch { }
		}
	}
}
