using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TPL
{
    /// <summary>
    /// Custom Decorator vẽ hình Squircle (superellipse n=4) thay vì rounded rectangle.
    /// Phương trình: |x|^4 + |y|^4 = r^4
    /// Dạng parametric: x(t) = r * sign(cos(t)) * sqrt(|cos(t)|)
    ///                  y(t) = r * sign(sin(t)) * sqrt(|sin(t)|)
    /// 
    /// Drop-in replacement cho Border trong XAML — chỉ cần đổi tag name.
    /// 
    /// Performance notes:
    /// - Geometry được cache, chỉ rebuild khi size/radius thay đổi.
    /// - Clip được set trong ArrangeOverride (KHÔNG trong OnRender) để tránh vòng lặp invalidation.
    /// - Pen được cache để tránh GC pressure.
    /// </summary>
    public class SquircleBorder : Decorator
    {
        // ── 12 segments/góc: đủ mượt cho mắt người, tổng 48 points ──
        private const int SegmentsPerCorner = 12;

        // ── Cached geometry — chỉ rebuild khi size hoặc radius thay đổi ──
        private StreamGeometry _cachedOuterGeometry;
        private StreamGeometry _cachedBorderGeometry;
        private double _cacheWidth, _cacheHeight;
        private double _cacheTL, _cacheTR, _cacheBR, _cacheBL;
        private double _cacheBorderWidth;
        private Pen _cachedPen;
        private Brush _cachedPenBrush;
        private double _cachedPenThickness;

        #region Dependency Properties

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(SquircleBorder),
                new FrameworkPropertyMetadata(0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnGeometryInvalidated));

        public static readonly DependencyProperty TopLeftRadiusProperty =
            DependencyProperty.Register(nameof(TopLeftRadius), typeof(double?), typeof(SquircleBorder),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnGeometryInvalidated));

        public static readonly DependencyProperty TopRightRadiusProperty =
            DependencyProperty.Register(nameof(TopRightRadius), typeof(double?), typeof(SquircleBorder),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnGeometryInvalidated));

        public static readonly DependencyProperty BottomLeftRadiusProperty =
            DependencyProperty.Register(nameof(BottomLeftRadius), typeof(double?), typeof(SquircleBorder),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnGeometryInvalidated));

        public static readonly DependencyProperty BottomRightRadiusProperty =
            DependencyProperty.Register(nameof(BottomRightRadius), typeof(double?), typeof(SquircleBorder),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnGeometryInvalidated));

        public static readonly DependencyProperty BackgroundProperty =
            Panel.BackgroundProperty.AddOwner(typeof(SquircleBorder),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BorderBrushProperty =
            Border.BorderBrushProperty.AddOwner(typeof(SquircleBorder),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BorderThicknessProperty =
            Border.BorderThicknessProperty.AddOwner(typeof(SquircleBorder),
                new FrameworkPropertyMetadata(new Thickness(0),
                    FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnGeometryInvalidated));

        public static readonly DependencyProperty PaddingProperty =
            Border.PaddingProperty.AddOwner(typeof(SquircleBorder),
                new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));

        private static void OnGeometryInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (SquircleBorder)d;
            self._cachedOuterGeometry = null;
            self._cachedBorderGeometry = null;
        }

        #endregion

        #region Properties

        /// <summary>Bán kính squircle mặc định cho tất cả 4 góc.</summary>
        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        /// <summary>Override bán kính cho góc trên-trái. Null = dùng CornerRadius chung.</summary>
        public double? TopLeftRadius
        {
            get => (double?)GetValue(TopLeftRadiusProperty);
            set => SetValue(TopLeftRadiusProperty, value);
        }

        /// <summary>Override bán kính cho góc trên-phải. Null = dùng CornerRadius chung.</summary>
        public double? TopRightRadius
        {
            get => (double?)GetValue(TopRightRadiusProperty);
            set => SetValue(TopRightRadiusProperty, value);
        }

        /// <summary>Override bán kính cho góc dưới-trái. Null = dùng CornerRadius chung.</summary>
        public double? BottomLeftRadius
        {
            get => (double?)GetValue(BottomLeftRadiusProperty);
            set => SetValue(BottomLeftRadiusProperty, value);
        }

        /// <summary>Override bán kính cho góc dưới-phải. Null = dùng CornerRadius chung.</summary>
        public double? BottomRightRadius
        {
            get => (double?)GetValue(BottomRightRadiusProperty);
            set => SetValue(BottomRightRadiusProperty, value);
        }

        public Brush Background
        {
            get => (Brush)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public Brush BorderBrush
        {
            get => (Brush)GetValue(BorderBrushProperty);
            set => SetValue(BorderBrushProperty, value);
        }

        public Thickness BorderThickness
        {
            get => (Thickness)GetValue(BorderThicknessProperty);
            set => SetValue(BorderThicknessProperty, value);
        }

        public Thickness Padding
        {
            get => (Thickness)GetValue(PaddingProperty);
            set => SetValue(PaddingProperty, value);
        }

        #endregion

        #region Layout

        protected override Size MeasureOverride(Size availableSize)
        {
            var child = Child;
            var borderThickness = BorderThickness;
            var padding = Padding;

            double extraWidth = borderThickness.Left + borderThickness.Right + padding.Left + padding.Right;
            double extraHeight = borderThickness.Top + borderThickness.Bottom + padding.Top + padding.Bottom;

            if (child != null)
            {
                var childAvailable = new Size(
                    Math.Max(0, availableSize.Width - extraWidth),
                    Math.Max(0, availableSize.Height - extraHeight));
                child.Measure(childAvailable);
                return new Size(
                    child.DesiredSize.Width + extraWidth,
                    child.DesiredSize.Height + extraHeight);
            }

            return new Size(extraWidth, extraHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var child = Child;
            if (child != null)
            {
                var borderThickness = BorderThickness;
                var padding = Padding;

                double left = borderThickness.Left + padding.Left;
                double top = borderThickness.Top + padding.Top;
                double right = borderThickness.Right + padding.Right;
                double bottom = borderThickness.Bottom + padding.Bottom;

                child.Arrange(new Rect(
                    left, top,
                    Math.Max(0, finalSize.Width - left - right),
                    Math.Max(0, finalSize.Height - top - bottom)));
            }

            // ★ Set Clip ở đây (KHÔNG trong OnRender) để tránh vòng lặp invalidation ★
            UpdateClipGeometry(finalSize);

            return finalSize;
        }

        private void UpdateClipGeometry(Size size)
        {
            double width = size.Width;
            double height = size.Height;
            if (width <= 0 || height <= 0) { Clip = null; return; }

            double topLeft = TopLeftRadius ?? CornerRadius;
            double topRight = TopRightRadius ?? CornerRadius;
            double bottomRight = BottomRightRadius ?? CornerRadius;
            double bottomLeft = BottomLeftRadius ?? CornerRadius;

            if (topLeft <= 0 && topRight <= 0 && bottomRight <= 0 && bottomLeft <= 0)
            {
                Clip = null;
                return;
            }

            double maxRadius = Math.Min(width / 2.0, height / 2.0);
            topLeft = Math.Min(topLeft, maxRadius);
            topRight = Math.Min(topRight, maxRadius);
            bottomRight = Math.Min(bottomRight, maxRadius);
            bottomLeft = Math.Min(bottomLeft, maxRadius);

            Clip = BuildSquircleGeometry(0, 0, width, height,
                topLeft, topRight, bottomRight, bottomLeft);
        }

        #endregion

        #region Rendering

        protected override void OnRender(DrawingContext drawingContext)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            var background = Background;
            var borderBrush = BorderBrush;
            var borderThickness = BorderThickness;
            double uniformBorder = Math.Max(Math.Max(borderThickness.Left, borderThickness.Right),
                                            Math.Max(borderThickness.Top, borderThickness.Bottom));

            // Resolve & clamp radii
            double topLeft = TopLeftRadius ?? CornerRadius;
            double topRight = TopRightRadius ?? CornerRadius;
            double bottomRight = BottomRightRadius ?? CornerRadius;
            double bottomLeft = BottomLeftRadius ?? CornerRadius;

            double maxRadius = Math.Min(width / 2.0, height / 2.0);
            topLeft = Math.Min(topLeft, maxRadius);
            topRight = Math.Min(topRight, maxRadius);
            bottomRight = Math.Min(bottomRight, maxRadius);
            bottomLeft = Math.Min(bottomLeft, maxRadius);

            // ★ Cache outer geometry — chỉ rebuild khi size/radius thực sự thay đổi ★
            if (_cachedOuterGeometry == null ||
                _cacheWidth != width || _cacheHeight != height ||
                _cacheTL != topLeft || _cacheTR != topRight ||
                _cacheBR != bottomRight || _cacheBL != bottomLeft)
            {
                _cachedOuterGeometry = BuildSquircleGeometry(0, 0, width, height,
                    topLeft, topRight, bottomRight, bottomLeft);
                _cacheWidth = width;
                _cacheHeight = height;
                _cacheTL = topLeft;
                _cacheTR = topRight;
                _cacheBR = bottomRight;
                _cacheBL = bottomLeft;
                _cachedBorderGeometry = null; // border cũng cần rebuild
            }

            // Fill background
            if (background != null)
            {
                drawingContext.DrawGeometry(background, null, _cachedOuterGeometry);
            }

            // Draw border stroke
            if (borderBrush != null && uniformBorder > 0)
            {
                // Cache Pen
                if (_cachedPen == null || _cachedPenBrush != borderBrush || _cachedPenThickness != uniformBorder)
                {
                    _cachedPen = new Pen(borderBrush, uniformBorder);
                    _cachedPen.Freeze();
                    _cachedPenBrush = borderBrush;
                    _cachedPenThickness = uniformBorder;
                }

                // Cache border geometry
                if (_cachedBorderGeometry == null || _cacheBorderWidth != uniformBorder)
                {
                    double halfBorder = uniformBorder / 2.0;
                    _cachedBorderGeometry = BuildSquircleGeometry(
                        halfBorder, halfBorder,
                        width - uniformBorder, height - uniformBorder,
                        Math.Max(0, topLeft - halfBorder),
                        Math.Max(0, topRight - halfBorder),
                        Math.Max(0, bottomRight - halfBorder),
                        Math.Max(0, bottomLeft - halfBorder));
                    _cacheBorderWidth = uniformBorder;
                }

                drawingContext.DrawGeometry(null, _cachedPen, _cachedBorderGeometry);
            }
        }

        #endregion

        #region Squircle Geometry — Tính trực tiếp, chính xác 100%

        /// <summary>
        /// Xây dựng StreamGeometry cho hình squircle với 4 góc có bán kính riêng.
        /// Sử dụng phương trình parametric superellipse n=4 trực tiếp.
        /// </summary>
        private static StreamGeometry BuildSquircleGeometry(
            double x, double y, double width, double height,
            double radiusTopLeft, double radiusTopRight,
            double radiusBottomRight, double radiusBottomLeft)
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                // Bắt đầu: đầu cạnh trên (sau góc top-left)
                context.BeginFigure(new Point(x + radiusTopLeft, y), isFilled: true, isClosed: true);

                // ── Cạnh trên → ──
                context.LineTo(new Point(x + width - radiusTopRight, y), isStroked: true, isSmoothJoin: true);

                // ── Góc top-right: angle -π/2 → 0 ──
                DrawSquircleCorner(context,
                    x + width - radiusTopRight, y + radiusTopRight,
                    radiusTopRight, -Math.PI / 2.0, 0);

                // ── Cạnh phải ↓ ──
                context.LineTo(new Point(x + width, y + height - radiusBottomRight), isStroked: true, isSmoothJoin: true);

                // ── Góc bottom-right: angle 0 → π/2 ──
                DrawSquircleCorner(context,
                    x + width - radiusBottomRight, y + height - radiusBottomRight,
                    radiusBottomRight, 0, Math.PI / 2.0);

                // ── Cạnh dưới ← ──
                context.LineTo(new Point(x + radiusBottomLeft, y + height), isStroked: true, isSmoothJoin: true);

                // ── Góc bottom-left: angle π/2 → π ──
                DrawSquircleCorner(context,
                    x + radiusBottomLeft, y + height - radiusBottomLeft,
                    radiusBottomLeft, Math.PI / 2.0, Math.PI);

                // ── Cạnh trái ↑ ──
                context.LineTo(new Point(x, y + radiusTopLeft), isStroked: true, isSmoothJoin: true);

                // ── Góc top-left: angle π → 3π/2 ──
                DrawSquircleCorner(context,
                    x + radiusTopLeft, y + radiusTopLeft,
                    radiusTopLeft, Math.PI, 3.0 * Math.PI / 2.0);
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>
        /// Vẽ một góc squircle (1/4 superellipse n=4).
        /// Dùng Math.Sqrt (= |cos|^0.5) thay vì Math.Pow cho tốc độ.
        /// </summary>
        private static void DrawSquircleCorner(
            StreamGeometryContext context,
            double centerX, double centerY,
            double radius,
            double startAngle, double endAngle)
        {
            if (radius <= 0) return;

            double angleStep = (endAngle - startAngle) / SegmentsPerCorner;

            for (int i = 1; i <= SegmentsPerCorner; i++)
            {
                double angle = startAngle + i * angleStep;
                double cosA = Math.Cos(angle);
                double sinA = Math.Sin(angle);

                // Superellipse n=4: exponent = 2/4 = 0.5 → dùng sqrt
                double pointX = centerX + radius * Math.Sign(cosA) * Math.Sqrt(Math.Abs(cosA));
                double pointY = centerY + radius * Math.Sign(sinA) * Math.Sqrt(Math.Abs(sinA));

                context.LineTo(new Point(pointX, pointY), isStroked: true, isSmoothJoin: true);
            }
        }

        #endregion
    }
}
