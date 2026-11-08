using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PdfiumViewer;

namespace TPL
{
	// Owns only the preview renderer's window hook. No global message filter or CAD API.
	internal sealed class PdfPreviewZoomController : NativeWindow, IDisposable
	{
		private const int MouseWheelMessage = 0x020A;
		private const int SetRedrawMessage = 0x000B;
		private const uint RedrawInvalidate = 0x0001;
		private const uint RedrawFrame = 0x0400;
		private const int ZoomIntervalMilliseconds = 15;
		private const int FallbackZoomIntervalMilliseconds = 80;
		private const int ZoomSettleMilliseconds = 140;
		private readonly PdfRenderer _renderer;
		private readonly Timer _timer;
		private IPdfDocument _pendingDocument;
		private double? _pendingZoom;
		private Point _anchor;
		private ZoomAnchor _pendingAnchor;
		private long _lastInputTime;
		private long _lastFrameTime;
		private bool _disposed;

		internal PdfPreviewZoomController(PdfRenderer renderer)
		{
			_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
			_timer = new Timer { Interval = ZoomIntervalMilliseconds };
			_timer.Tick += ApplyPendingZoom;
			_renderer.HandleCreated += RendererHandleCreated;
			_renderer.HandleDestroyed += RendererHandleDestroyed;
			if (_renderer.IsHandleCreated) AssignHandle(_renderer.Handle);
		}

		internal double RequestedZoom => _pendingDocument == _renderer.Document ? _pendingZoom ?? _renderer.Zoom : _renderer.Zoom;

		internal void RequestZoom(double zoom, Point anchor)
		{
			if (_disposed || _renderer.IsDisposed || _renderer.Document == null ||
				_renderer.ClientSize.Width <= 0 || _renderer.ClientSize.Height <= 0 ||
				double.IsNaN(zoom) || double.IsInfinity(zoom)) return;
			if (_pendingDocument != null && _pendingDocument != _renderer.Document) CancelPendingZoom();
			double target = Math.Max(_renderer.ZoomMin, Math.Min(zoom, _renderer.ZoomMax));
			if (Math.Abs(target - RequestedZoom) < 0.0001) return;

			if (!_pendingZoom.HasValue || anchor != _anchor)
				_pendingAnchor = CaptureAnchor(anchor);
			if (!_pendingAnchor.IsValid) return;
			_pendingDocument = _renderer.Document;
			_pendingZoom = target;
			_anchor = anchor;
			_lastInputTime = Stopwatch.GetTimestamp();
			bool canAnimate = (_renderer as PdfPreviewRenderer)?.BeginInteractiveZoom() == true;
			_timer.Interval = canAnimate ? ZoomIntervalMilliseconds : FallbackZoomIntervalMilliseconds;
			if (!_timer.Enabled)
			{
				_lastFrameTime = _lastInputTime;
				_timer.Start();
			}
		}

		internal void CancelPendingZoom()
		{
			if (_disposed) return;
			_timer.Stop();
			_pendingZoom = null;
			_pendingDocument = null;
			(_renderer as PdfPreviewRenderer)?.EndInteractiveZoom();
		}

		internal void ApplyZoom(double requestedZoom, Point requestedAnchor)
		{
			if (_disposed || _renderer.IsDisposed || _renderer.Document == null ||
				_renderer.ClientSize.Width <= 0 || _renderer.ClientSize.Height <= 0 ||
				double.IsNaN(requestedZoom) || double.IsInfinity(requestedZoom)) return;

			ApplyZoom(requestedZoom, CaptureAnchor(requestedAnchor));
		}

		private ZoomAnchor CaptureAnchor(Point requestedAnchor)
		{
			Point anchor = requestedAnchor;
			PdfPoint pdfPoint = _renderer.ClientRectangle.Contains(anchor) ? _renderer.PointToPdf(anchor) : PdfPoint.Empty;
			if (!pdfPoint.IsValid)
			{
				// The pointer must be over the page itself, not the surrounding surface.
				anchor = new Point(_renderer.ClientSize.Width / 2, _renderer.ClientSize.Height / 2);
				pdfPoint = _renderer.PointToPdf(anchor);
			}

			Rectangle oldBounds = pdfPoint.IsValid ? GetPageBounds(pdfPoint.Page) : _renderer.DisplayRectangle;
			if (oldBounds.Width <= 0 || oldBounds.Height <= 0) return default;
			// Keep a page-relative fraction instead of PointToPdf's integer-rounded
			// coordinates, which drift noticeably at high zoom near the page edges.
			return new ZoomAnchor(anchor, pdfPoint.IsValid ? pdfPoint.Page : -1,
				(anchor.X - oldBounds.Left) / (double)oldBounds.Width,
				(anchor.Y - oldBounds.Top) / (double)oldBounds.Height);
		}

		private void ApplyZoom(double requestedZoom, ZoomAnchor anchor)
		{
			if (!anchor.IsValid) return;
			double targetZoom = Math.Max(_renderer.ZoomMin, Math.Min(requestedZoom, _renderer.ZoomMax));
			if (Math.Abs(targetZoom - _renderer.Zoom) < 0.0001) return;

			// Updating zoom can change scrollbar visibility and scroll the old bitmap.
			// Present only the final layout and anchor correction as a single repaint.
			IntPtr redrawHandle = _renderer.IsHandleCreated && _renderer.Visible ? _renderer.Handle : IntPtr.Zero;
			try
			{
				if (redrawHandle != IntPtr.Zero) SendMessage(redrawHandle, SetRedrawMessage, IntPtr.Zero, IntPtr.Zero);
				_renderer.Zoom = targetZoom;
				Rectangle display = _renderer.DisplayRectangle;
				Rectangle newBounds = anchor.Page >= 0 ? GetPageBounds(anchor.Page) : display;
				_renderer.SetDisplayRectLocation(new Point(
					display.Left + (int)Math.Round(anchor.Location.X - newBounds.Left - anchor.RelativeX * newBounds.Width),
					display.Top + (int)Math.Round(anchor.Location.Y - newBounds.Top - anchor.RelativeY * newBounds.Height)), false);
			}
			finally
			{
				if (redrawHandle != IntPtr.Zero && _renderer.IsHandleCreated && _renderer.Handle == redrawHandle)
				{
					SendMessage(redrawHandle, SetRedrawMessage, new IntPtr(1), IntPtr.Zero);
					RedrawWindow(redrawHandle, IntPtr.Zero, IntPtr.Zero, RedrawInvalidate | RedrawFrame);
				}
				else if (!_renderer.IsDisposed)
				{
					_renderer.Invalidate();
				}
			}
		}

		private Rectangle GetPageBounds(int page)
		{
			return PdfPreviewRenderer.GetPageClientBounds(_renderer, page);
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);

		protected override void WndProc(ref Message message)
		{
			try
			{
				bool wheelZoom = _renderer.MouseWheelMode == MouseWheelMode.Zoom ||
					(_renderer.MouseWheelMode == MouseWheelMode.PanAndZoom && (Control.ModifierKeys & Keys.Control) != 0);
				if (!_disposed && message.Msg == MouseWheelMessage && wheelZoom && _renderer.Document != null)
				{
					int delta = unchecked((short)(message.WParam.ToInt64() >> 16));
					if (delta != 0)
					{
						long position = message.LParam.ToInt64();
						var screenPoint = new Point(unchecked((short)position), unchecked((short)(position >> 16)));
						RequestZoom(RequestedZoom * Math.Pow(_renderer.ZoomFactor, delta / 120.0),
							_renderer.PointToClient(screenPoint));
					}
					message.Result = IntPtr.Zero;
					return;
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[PdfEditor] Preview mouse-wheel zoom: {ex}");
			}
			base.WndProc(ref message);
		}

		private void ApplyPendingZoom(object sender, EventArgs e)
		{
			if (_disposed || !_pendingZoom.HasValue || _pendingDocument != _renderer.Document || _renderer.IsDisposed)
			{
				CancelPendingZoom();
				return;
			}
			try
			{
				long now = Stopwatch.GetTimestamp();
				bool canAnimate = (_renderer as PdfPreviewRenderer)?.BeginInteractiveZoom() == true;
				_timer.Interval = canAnimate ? ZoomIntervalMilliseconds : FallbackZoomIntervalMilliseconds;
				double target = _pendingZoom.Value;
				double zoom = target;
				if (canAnimate)
				{
					double elapsed = (now - _lastFrameTime) * 1000.0 / Stopwatch.Frequency;
					double distance = Math.Log(target / _renderer.Zoom);
					if (Math.Abs(distance) > 0.001)
						zoom = _renderer.Zoom * Math.Exp(distance * (1 - Math.Exp(-elapsed / 35.0)));
				}
				_lastFrameTime = now;
				ApplyZoom(zoom, _pendingAnchor);
				if (!canAnimate || (Math.Abs(_renderer.Zoom - target) < 0.0001 &&
					(now - _lastInputTime) * 1000.0 / Stopwatch.Frequency >= ZoomSettleMilliseconds))
					CancelPendingZoom();
			}
			catch (Exception ex)
			{
				CancelPendingZoom();
				System.Diagnostics.Trace.WriteLine($"[PdfEditor] Apply preview zoom: {ex}");
			}
		}

		private void RendererHandleCreated(object sender, EventArgs e)
		{
			try { if (!_disposed) AssignHandle(_renderer.Handle); }
			catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[PdfEditor] Attach preview zoom hook: {ex}"); }
		}

		private void RendererHandleDestroyed(object sender, EventArgs e)
		{
			try
			{
				CancelPendingZoom();
				ReleaseHandle();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[PdfEditor] Detach preview zoom hook: {ex}");
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			CancelPendingZoom();
			_disposed = true;
			_renderer.HandleCreated -= RendererHandleCreated;
			_renderer.HandleDestroyed -= RendererHandleDestroyed;
			_timer.Tick -= ApplyPendingZoom;
			_timer.Dispose();
			ReleaseHandle();
		}

		private readonly struct ZoomAnchor
		{
			internal Point Location { get; }
			internal int Page { get; }
			internal double RelativeX { get; }
			internal double RelativeY { get; }
			internal bool IsValid { get; }

			internal ZoomAnchor(Point location, int page, double relativeX, double relativeY)
			{
				Location = location;
				Page = page;
				RelativeX = relativeX;
				RelativeY = relativeY;
				IsValid = true;
			}
		}
	}
}
