using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PdfiumViewer;

namespace TPL
{
	internal class PdfPreviewRenderer : PdfRenderer
	{
		private const int MaximumPreviewDimension = 1600;
		private const int MaximumPreviewPixels = 1500000;
		private static readonly SemaphoreSlim PreviewRenderGate = new(1, 1);
		private CancellationTokenSource _previewCancellation;
		private Bitmap _zoomPreview;
		private IPdfDocument _previewDocument;
		private bool _interactiveZoom;

		internal bool IsInteractiveZoom => _interactiveZoom && _zoomPreview != null && _previewDocument == Document;

		// The editor supplies an immutable snapshot of its single-page PDF. A separate
		// PDFium document owns all background work; no UI document or CAD API is used.
		internal async Task PrepareZoomPreviewAsync(byte[] previewData)
		{
			ClearZoomPreview();
			if (IsDisposed || Document == null || Document.PageCount != 1) return;
			var cancellation = new CancellationTokenSource();
			_previewCancellation = cancellation;
			IPdfDocument document = Document;
			Bitmap preview = null;
			try
			{
				preview = await RenderZoomPreviewAsync(previewData, cancellation.Token);
				if (IsDisposed || cancellation.IsCancellationRequested || document != Document || _previewCancellation != cancellation) return;
				_zoomPreview = preview;
				_previewDocument = document;
				preview = null;
				if (_interactiveZoom) Invalidate();
			}
			catch (OperationCanceledException) { return; }
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"[PdfEditor] Prepare zoom preview: {ex}");
			}
			finally
			{
				preview?.Dispose();
				if (_previewCancellation == cancellation) _previewCancellation = null;
				cancellation.Dispose();
			}
		}

		private static async Task<Bitmap> RenderZoomPreviewAsync(byte[] previewData, CancellationToken cancellationToken)
		{
			await PreviewRenderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				return await Task.Run(() =>
				{
					cancellationToken.ThrowIfCancellationRequested();
					using var stream = new MemoryStream(previewData, writable: false);
					using var document = PdfDocument.Load(stream);
					SizeF size = document.PageSizes[0];
					double scale = Math.Min(MaximumPreviewDimension / (double)Math.Max(size.Width, size.Height),
						Math.Sqrt(MaximumPreviewPixels / ((double)size.Width * size.Height)));
					int width = Math.Max(1, (int)(size.Width * scale));
					int height = Math.Max(1, (int)(size.Height * scale));
					cancellationToken.ThrowIfCancellationRequested();
					return (Bitmap)document.Render(0, width, height, 96, 96, PdfRenderFlags.Annotations);
				}, cancellationToken).ConfigureAwait(false);
			}
			finally { PreviewRenderGate.Release(); }
		}

		internal bool BeginInteractiveZoom()
		{
			_interactiveZoom = true;
			return IsInteractiveZoom;
		}

		internal void EndInteractiveZoom()
		{
			if (!_interactiveZoom) return;
			_interactiveZoom = false;
			if (!IsDisposed) Invalidate();
		}

		internal void ClearZoomPreview()
		{
			_previewCancellation?.Cancel();
			_previewCancellation = null;
			_interactiveZoom = false;
			_previewDocument = null;
			_zoomPreview?.Dispose();
			_zoomPreview = null;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			try
			{
				if (IsInteractiveZoom) PaintZoomPreview(e);
				else base.OnPaint(e);
			}
			catch (Exception ex)
			{
				ClearZoomPreview();
				System.Diagnostics.Trace.WriteLine($"[PdfEditor] Paint PDF preview: {ex}");
			}
		}

		private void PaintZoomPreview(PaintEventArgs e)
		{
			Rectangle pageBounds = GetPageClientBounds(this, 0);
			GraphicsState state = e.Graphics.Save();
			try
			{
				using var background = new SolidBrush(BackColor);
				e.Graphics.FillRectangle(background, e.ClipRectangle);
				e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
				e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
				e.Graphics.DrawImage(_zoomPreview, pageBounds, 0, 0, _zoomPreview.Width, _zoomPreview.Height, GraphicsUnit.Pixel);
			}
			finally { e.Graphics.Restore(state); }
		}

		internal static Rectangle GetPageClientBounds(PdfRenderer renderer, int page)
		{
			SizeF size = renderer.Document.PageSizes[page];
			// PageSizes describes the rendered page. Convert back to PDF coordinates
			// first so inherited page rotation and crop offsets are respected.
			RectangleF pdfBounds = renderer.Document.RectangleToPdf(page,
				new Rectangle(0, 0, Math.Max(1, (int)size.Width), Math.Max(1, (int)size.Height)));
			return renderer.BoundsFromPdf(new PdfRectangle(page, pdfBounds));
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) ClearZoomPreview();
			base.Dispose(disposing);
		}
	}

	// Only the renderer is needed: the editor owns its WPF toolbar and page list.
	internal sealed class PdfPreviewViewer : UserControl
	{
		private IPdfDocument _document;
		internal PdfPreviewRenderer Renderer { get; } = new() { Dock = DockStyle.Fill, ZoomMode = PdfViewerZoomMode.FitHeight };

		internal IPdfDocument Document
		{
			get => _document;
			set
			{
				if (_document == value) return;
				Renderer.ClearZoomPreview();
				if (value != null) Renderer.Load(value);
				_document = value;
				Renderer.Visible = value != null;
			}
		}

		internal PdfPreviewViewer()
		{
			Renderer.Visible = false;
			Controls.Add(Renderer);
		}
	}
}
