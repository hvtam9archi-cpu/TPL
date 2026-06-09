using System.ComponentModel;
using System.Windows.Media;

namespace TPL.Presentation.ViewModels
{
	/// <summary>
	/// Model cho mỗi trang PDF trong danh sách.
	/// Implements INotifyPropertyChanged để cập nhật STT khi thay đổi vị trí.
	/// </summary>
	public class PdfPageViewModel : INotifyPropertyChanged
	{
		private int _sequenceNumber;

		public string SourceFile { get; set; }
		public int PageIndex { get; set; }
		public int RotationDelta { get; set; } = 0;
		public System.Drawing.Color GroupColor { get; set; }
		public string DisplayName { get; set; }

		/// <summary>Cached single-page PDF bytes for instant preview.</summary>
		public byte[] PreviewData { get; set; }

		/// <summary>Nhãn nguồn gốc: "Plot" cho xuất từ AutoCAD, "File" cho thêm từ file.</summary>
		public string SourceLabel { get; set; } = "Plot";

		/// <summary>Mã nhóm để nhận diện batch (mỗi lần Plot hoặc Add file là 1 group).</summary>
		public int GroupId { get; set; }

		public int SequenceNumber
		{
			get => _sequenceNumber;
			set { _sequenceNumber = value; OnPropertyChanged(nameof(SequenceNumber)); }
		}

		/// <summary>Brush WPF dùng để hiển thị color indicator trong ListView.</summary>
		private SolidColorBrush _groupColorBrush;
		public SolidColorBrush GroupColorBrush =>
			_groupColorBrush ?? (_groupColorBrush = new SolidColorBrush(
				Color.FromArgb(GroupColor.A, GroupColor.R, GroupColor.G, GroupColor.B)));

		/// <summary>Màu nền nhạt (~18% opacity) để phân biệt rõ các nhóm PDF khác nhau.</summary>
		private SolidColorBrush _rowBackgroundColorBrush;
		public SolidColorBrush RowBackgroundColorBrush =>
			_rowBackgroundColorBrush ?? (_rowBackgroundColorBrush = new SolidColorBrush(
				Color.FromArgb(45, GroupColor.R, GroupColor.G, GroupColor.B)));

		/// <summary>Màu nền khi hover — giữ tint nhóm + tăng sáng nhẹ.</summary>
		private SolidColorBrush _hoverBackgroundBrush;
		public SolidColorBrush HoverBackgroundBrush =>
			_hoverBackgroundBrush ?? (_hoverBackgroundBrush = new SolidColorBrush(
				Color.FromArgb(70, GroupColor.R, GroupColor.G, GroupColor.B)));

		/// <summary>Màu nền khi selected — blend accent blue + tint nhóm.</summary>
		private SolidColorBrush _selectedBackgroundBrush;
		public SolidColorBrush SelectedBackgroundBrush
		{
			get
			{
				if (_selectedBackgroundBrush != null) return _selectedBackgroundBrush;
				byte r = (byte)((GroupColor.R * 0.4) + (0x25 * 0.6));
				byte g = (byte)((GroupColor.G * 0.4) + (0x63 * 0.6));
				byte b = (byte)((GroupColor.B * 0.4) + (0xEB * 0.6));
				_selectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(50, r, g, b));
				return _selectedBackgroundBrush;
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
		protected void OnPropertyChanged(string name) =>
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
