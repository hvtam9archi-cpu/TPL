using System.Windows;
using System.Windows.Input;
using TPL.Presentation.ViewModels;

namespace TPL.Presentation.Views
{
	public partial class LicenseWindow : Window
	{
		private readonly LicenseWindowViewModel _vm;

		public LicenseWindow(LicenseWindowViewModel vm)
		{
			_vm = vm;
			DataContext = _vm;
			InitializeComponent();

			this.Loaded += (s, e) => _vm.LoadLicenseInfo();
		}

		private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

		private void BtnCopy_Click(object sender, RoutedEventArgs e) => _vm.CopyHardwareIdCommand.Execute(null);

		private void BtnActivate_Click(object sender, RoutedEventArgs e)
		{
			_vm.ActivateLicenseCommand.Execute(null);
			if (_vm.IsLicenseValid)
				this.DialogResult = true;
		}

		private void BtnClose_Click(object sender, RoutedEventArgs e)
		{
			this.DialogResult = _vm.IsLicenseValid;
			this.Close();
		}
	}
}
