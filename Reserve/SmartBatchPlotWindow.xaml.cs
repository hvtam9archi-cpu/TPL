using System.Windows;
using System.Windows.Input;

namespace TPL
{
    public partial class SmartBatchPlotWindow : Window
    {
        public SmartBatchPlotWindow()
        {
            InitializeComponent();
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

        private void BtnFramesLibrary_Click(object sender, RoutedEventArgs e)
        {
            var framesLibWin = new FramesLibraryWindow();
            framesLibWin.Owner = this;
            framesLibWin.ShowDialog();
        }
    }
}
