using System;
using System.Reflection;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TPL.Core.Helpers
{
	/// <summary>
	/// Tiện ích load ảnh từ embedded resource — trích xuất từ RibbonSetup.cs.
	/// Force resize bằng System.Drawing để chống lỗi scale/crop của AutoCAD.
	/// </summary>
	public static class ImageHelper
	{
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
		private static extern bool DeleteObject(IntPtr hObject);

		/// <summary>
		/// Load ảnh PNG từ assembly manifest stream và force resize.
		/// </summary>
		/// <param name="assembly">Assembly chứa resource.</param>
		/// <param name="resourceName">Tên resource (VD: "TPL.Resource.IconRibbon_32px.png").</param>
		/// <param name="size">Kích thước đích (32 hoặc 16).</param>
		/// <returns>ImageSource hoặc null nếu lỗi.</returns>
		public static ImageSource LoadEmbeddedImage(Assembly assembly, string resourceName, int size)
		{
			try
			{
				using var stream = assembly.GetManifestResourceStream(resourceName);
				if (stream == null) return null;

				using var drawingImg = System.Drawing.Image.FromStream(stream);
				using var bmp = new System.Drawing.Bitmap(drawingImg, new System.Drawing.Size(size, size));
				IntPtr hBitmap = bmp.GetHbitmap();
				try
				{
					var source = Imaging.CreateBitmapSourceFromHBitmap(
						hBitmap,
						IntPtr.Zero,
						System.Windows.Int32Rect.Empty,
						BitmapSizeOptions.FromEmptyOptions());

					source.Freeze();
					return source;
				}
				finally
				{
					DeleteObject(hBitmap);
				}
			}
			catch
			{
				return null;
			}
		}
	}
}
