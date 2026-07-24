using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Pixelate.Net.Avalonia.Controls;

/// <summary>
/// 像素显示模式。
/// </summary>
public enum DisplayMode
{
    /// <summary>方珠：每个像素渲染成正方形，1 像素分割线。</summary>
    Square,

    /// <summary>圆珠：每个像素渲染成圆形，无分割线。</summary>
    Round,

    /// <summary>空珠：每个像素渲染成空心圆环，无分割线。</summary>
    Hollow
}

/// <summary>
/// 像素点击事件参数。
/// </summary>
public class PixelClickedEventArgs(int x, int y) : EventArgs
{
    public int X { get; } = x;
    public int Y { get; } = y;
}

/// <summary>
/// 渲染像素化结果的自定义控件。
/// 直接绘制色块（正方形/圆形），保证分割线在任意尺寸下都是 1 屏幕像素宽。
/// </summary>
public class PixelGridControl : Control
{
    public static readonly StyledProperty<byte[]?> PixelDataProperty =
        AvaloniaProperty.Register<PixelGridControl, byte[]?>(nameof(PixelData));

    public static readonly StyledProperty<int> GridWidthProperty =
        AvaloniaProperty.Register<PixelGridControl, int>(nameof(GridWidth));

    public static readonly StyledProperty<int> GridHeightProperty =
        AvaloniaProperty.Register<PixelGridControl, int>(nameof(GridHeight));

    public static readonly StyledProperty<DisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<PixelGridControl, DisplayMode>(nameof(DisplayMode));

    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<PixelGridControl, bool>(nameof(IsEditable));

    public static readonly StyledProperty<bool> ShowCodesProperty =
        AvaloniaProperty.Register<PixelGridControl, bool>(nameof(ShowCodes));

    public static readonly StyledProperty<IReadOnlyDictionary<uint, string>?> ColorCodeMapProperty =
        AvaloniaProperty.Register<PixelGridControl, IReadOnlyDictionary<uint, string>?>(nameof(ColorCodeMap));

    public static readonly StyledProperty<bool> IsEyedroppingProperty =
        AvaloniaProperty.Register<PixelGridControl, bool>(nameof(IsEyedropping));

    /// <summary>缩放比例（1.0=100% 适应画布，最大 5.0=500%）。</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PixelGridControl, double>(nameof(Zoom), defaultValue: 1.0);

    /// <summary>最小缩放比例（100%）。</summary>
    public const double MinZoom = 1.0;

    /// <summary>最大缩放比例（500%）。</summary>
    public const double MaxZoom = 5.0;

    /// <summary>像素被点击时触发（仅在 IsEditable=true 时）。</summary>
    public event EventHandler<PixelClickedEventArgs>? PixelClicked;

    static PixelGridControl()
    {
        AffectsRender<PixelGridControl>(PixelDataProperty, GridWidthProperty, GridHeightProperty, DisplayModeProperty, ShowCodesProperty, ColorCodeMapProperty, ZoomProperty);
        AffectsMeasure<PixelGridControl>(ZoomProperty);
        IsEditableProperty.Changed.AddClassHandler<PixelGridControl>((c, e) => c.UpdateCursor());
        IsEyedroppingProperty.Changed.AddClassHandler<PixelGridControl>((c, e) => c.UpdateCursor());
    }

    public PixelGridControl()
    {
        // 捕获实际视口尺寸。ScrollViewer(Auto) 会以 PositiveInfinity 测量子控件，
        // 此时无法从 availableSize 得知画布大小，需通过有效视口事件获取，用于计算"100% 适应画布"的基准尺寸。
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var vp = e.EffectiveViewport.Size;
        if (vp.Width > 0 && vp.Height > 0 && vp != _viewportSize)
        {
            _viewportSize = vp;
            InvalidateMeasure();
        }
    }

    /// <summary>最近一次有效视口尺寸（即外层 ScrollViewer 的可视区域），用于在无穷大测量约束下计算适应尺寸。</summary>
    private Size _viewportSize;

    // 颜色 → 画刷缓存，避免每帧重复创建大量 SolidColorBrush。
    private readonly Dictionary<uint, IBrush> _brushCache = new();
    private static readonly IBrush GridBrush = new SolidColorBrush(0x88888888);
    private static readonly IPen GridPen = new Pen(GridBrush, 1);
    // 被删除像素（alpha=0）的棋盘格颜色：白 + 浅灰，组成 2x2 棋盘表示透明
    private static readonly IBrush CheckerLightBrush = new SolidColorBrush(0xFFFFFFFF);
    private static readonly IBrush CheckerDarkBrush = new SolidColorBrush(0xFFCCCCCC);

    public byte[]? PixelData
    {
        get => GetValue(PixelDataProperty);
        set => SetValue(PixelDataProperty, value);
    }

    public int GridWidth
    {
        get => GetValue(GridWidthProperty);
        set => SetValue(GridWidthProperty, value);
    }

    public int GridHeight
    {
        get => GetValue(GridHeightProperty);
        set => SetValue(GridHeightProperty, value);
    }

    public DisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>是否允许点击编辑像素。</summary>
    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>是否在每个像素上叠加显示色卡编码。</summary>
    public bool ShowCodes
    {
        get => GetValue(ShowCodesProperty);
        set => SetValue(ShowCodesProperty, value);
    }

    /// <summary>RGB→编码映射，key = (R<<16)|(G<<8)|B。</summary>
    public IReadOnlyDictionary<uint, string>? ColorCodeMap
    {
        get => GetValue(ColorCodeMapProperty);
        set => SetValue(ColorCodeMapProperty, value);
    }

    /// <summary>是否处于取色模式（影响光标显示）。</summary>
    public bool IsEyedropping
    {
        get => GetValue(IsEyedroppingProperty);
        set => SetValue(IsEyedroppingProperty, value);
    }

    /// <summary>缩放比例，范围 [MinZoom, MaxZoom]（1.0=100%，5.0=500%）。</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom));
    }

    private void UpdateCursor()
    {
        if (!IsEditable)
            Cursor = null;
        else if (IsEyedropping)
            Cursor = new Cursor(StandardCursorType.Hand);
        else
            Cursor = new Cursor(StandardCursorType.Cross);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var data = PixelData;
        if (data == null || GridWidth <= 0 || GridHeight <= 0)
            return;
        if (data.Length < GridWidth * GridHeight * 4)
            return;

        var (offsetX, offsetY, cw, ch) = GetDrawMetrics();
        if (cw <= 0 || ch <= 0) return;

        using (context.PushTransform(Matrix.CreateTranslation(offsetX, offsetY)))
        {
            if (DisplayMode == DisplayMode.Square)
            {
                // 绘制色块（被删除像素统一渲染为 2x2 棋盘格，不受显示模式影响）
                for (int y = 0; y < GridHeight; y++)
                {
                    for (int x = 0; x < GridWidth; x++)
                    {
                        int i = (y * GridWidth + x) * 4;
                        var rect = new Rect(x * cw, y * ch, cw, ch);
                        if (data[i + 3] == 0)
                        {
                            DrawCheckerboard(context, rect);
                        }
                        else
                        {
                            var brush = GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                            context.DrawRectangle(brush, null, rect);
                        }
                    }
                }

                // 绘制 1px 分割线
                for (int x = 0; x <= GridWidth; x++)
                {
                    double px = x * cw;
                    context.DrawLine(GridPen, new Point(px, 0), new Point(px, GridHeight * ch));
                }
                for (int y = 0; y <= GridHeight; y++)
                {
                    double py = y * ch;
                    context.DrawLine(GridPen, new Point(0, py), new Point(GridWidth * cw, py));
                }
            }
            else if (DisplayMode == DisplayMode.Round) // 圆珠
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    for (int x = 0; x < GridWidth; x++)
                    {
                        int i = (y * GridWidth + x) * 4;
                        var rect = new Rect(x * cw, y * ch, cw, ch);
                        if (data[i + 3] == 0)
                        {
                            // 被删除像素不受显示模式影响，永远渲染为棋盘格方格
                            DrawCheckerboard(context, rect);
                        }
                        else
                        {
                            var brush = GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                            context.DrawEllipse(brush, null, rect);
                        }
                    }
                }
            }
            else // 空珠
            {
                double strokeWidth = Math.Max(1, Math.Min(cw, ch) / 6);
                double inset = strokeWidth / 2;
                for (int y = 0; y < GridHeight; y++)
                {
                    for (int x = 0; x < GridWidth; x++)
                    {
                        int i = (y * GridWidth + x) * 4;
                        var rect = new Rect(x * cw, y * ch, cw, ch);
                        if (data[i + 3] == 0)
                        {
                            // 被删除像素不受显示模式影响，永远渲染为棋盘格方格
                            DrawCheckerboard(context, rect);
                            continue;
                        }
                        var brush = GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                        context.DrawEllipse(null, new Pen(brush, strokeWidth),
                            new Rect(rect.X + inset, rect.Y + inset, cw - strokeWidth, ch - strokeWidth));
                    }
                }
            }

            if (ShowCodes && ColorCodeMap is not null && cw >= 16 && ch >= 10)
            {
                var typeface = new Typeface("Segoe UI");
                const double fontSize = 9;
                for (int y = 0; y < GridHeight; y++)
                {
                    for (int x = 0; x < GridWidth; x++)
                    {
                        int i = (y * GridWidth + x) * 4;
                        byte r = data[i], g = data[i + 1], b = data[i + 2];
                        // 跳过已删除的像素（alpha=0）
                        if (data[i + 3] == 0) continue;
                        uint key = ((uint)r << 16) | ((uint)g << 8) | b;
                        if (!ColorCodeMap.TryGetValue(key, out var code))
                            continue;

                        double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                        var foreground = brightness > 128 ? Brushes.Black : Brushes.White;

                        var formattedText = new FormattedText(
                            code,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            foreground);

                        double textX = x * cw + (cw - formattedText.Width) / 2;
                        double textY = y * ch + (ch - formattedText.Height) / 2;
                        context.DrawText(formattedText, new Point(textX, textY));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 计算绘制区域的偏移量和每像素的宽高，保持纵横比居中。
    /// 供 Render 和 PointToPixel 共用，保证坐标一致性。
    /// </summary>
    private (double offsetX, double offsetY, double cw, double ch) GetDrawMetrics()
    {
        double boundsW = Bounds.Width;
        double boundsH = Bounds.Height;
        if (boundsW <= 0 || boundsH <= 0 || GridWidth <= 0 || GridHeight <= 0)
            return (0, 0, 0, 0);

        double aspectGrid = (double)GridWidth / GridHeight;
        double aspectBounds = boundsW / boundsH;
        double drawW, drawH;
        if (aspectGrid > aspectBounds)
        {
            drawW = boundsW;
            drawH = boundsW / aspectGrid;
        }
        else
        {
            drawH = boundsH;
            drawW = boundsH * aspectGrid;
        }
        double offsetX = (boundsW - drawW) / 2;
        double offsetY = (boundsH - drawH) / 2;
        double cw = drawW / GridWidth;
        double ch = drawH / GridHeight;
        return (offsetX, offsetY, cw, ch);
    }

    /// <summary>将控件内坐标转换为像素网格坐标，越界返回 null。</summary>
    private (int x, int y)? PointToPixel(Point point)
    {
        var (offsetX, offsetY, cw, ch) = GetDrawMetrics();
        if (cw <= 0 || ch <= 0) return null;

        double relX = point.X - offsetX;
        double relY = point.Y - offsetY;
        if (relX < 0 || relY < 0) return null;

        int px = (int)(relX / cw);
        int py = (int)(relY / ch);
        if (px < 0 || px >= GridWidth || py < 0 || py >= GridHeight) return null;
        return (px, py);
    }

    /// <summary>
    /// 测量：根据可用空间计算"适应尺寸"（保持纵横比），再乘以缩放比例。
    /// zoom=1 时返回可用空间尺寸（填满画布、居中显示）；zoom>1 时返回超出可用空间的尺寸，
    /// 触发外层 ScrollViewer 显示滚动条。
    /// 注意：ScrollViewer(Auto) 会以 PositiveInfinity 测量子控件，此时需用捕获的视口尺寸
    /// 作为基准，否则无法计算"100% 适应画布"的尺寸。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (GridWidth <= 0 || GridHeight <= 0 || PixelData is null)
            return base.MeasureOverride(availableSize);

        // 可用空间为无穷时（ScrollViewer 可滚动方向），改用捕获的视口尺寸计算适应尺寸。
        var effective = GetEffectiveAvailable(availableSize);
        var (fitW, fitH) = ComputeFitSize(effective);
        if (fitW <= 0 || fitH <= 0)
            return base.MeasureOverride(availableSize);

        double z = Zoom;
        double contentW = fitW * z;
        double contentH = fitH * z;
        // 可用空间有限时至少填满（zoom=1 居中）；为无穷时直接使用内容尺寸（zoom>1 超出视口触发滚动）。
        double w = double.IsInfinity(availableSize.Width) ? contentW : Math.Max(contentW, availableSize.Width);
        double h = double.IsInfinity(availableSize.Height) ? contentH : Math.Max(contentH, availableSize.Height);
        return new Size(w, h);
    }

    /// <summary>
    /// 获取用于计算"适应尺寸"的有效可用空间。
    /// 当外层 ScrollViewer 以无穷大测量本控件时，使用捕获的视口尺寸（或祖先 ScrollViewer 的 Viewport）替代。
    /// </summary>
    private Size GetEffectiveAvailable(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height))
            return availableSize;

        if (_viewportSize.Width > 0 && _viewportSize.Height > 0)
            return _viewportSize;

        var sv = FindScrollViewer();
        if (sv is not null && sv.Viewport.Width > 0 && sv.Viewport.Height > 0)
            return sv.Viewport;

        return availableSize;
    }

    /// <summary>计算在指定可用空间内保持纵横比的"适应尺寸"。</summary>
    private (double fitW, double fitH) ComputeFitSize(Size available)
    {
        double aw = available.Width;
        double ah = available.Height;
        if (double.IsInfinity(aw) && double.IsInfinity(ah))
            return (GridWidth, GridHeight);
        if (double.IsInfinity(aw)) aw = ah * GridWidth / (double)GridHeight;
        if (double.IsInfinity(ah)) ah = aw * GridHeight / (double)GridWidth;
        if (aw <= 0 || ah <= 0) return (0, 0);

        double aspectGrid = (double)GridWidth / GridHeight;
        double aspectBounds = aw / ah;
        if (aspectGrid > aspectBounds)
            return (aw, aw / aspectGrid);
        else
            return (ah * aspectGrid, ah);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        // 右键拖动平移：仅当内容超出视口时启用
        if (point.Properties.IsRightButtonPressed)
        {
            var sv = FindScrollViewer();
            if (sv is not null && IsOverflow(sv))
            {
                _isPanning = true;
                _panStartViewportPoint = e.GetPosition(sv);
                _panStartOffset = sv.Offset;
                e.Pointer.Capture(this);
                e.Handled = true;
                Cursor = new Cursor(StandardCursorType.SizeAll);
            }
            return;
        }

        if (!IsEditable) return;

        var p = e.GetPosition(this);
        if (PointToPixel(p) is (int px, int py))
        {
            e.Handled = true;
            PixelClicked?.Invoke(this, new PixelClickedEventArgs(px, py));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPanning) return;

        var sv = FindScrollViewer();
        if (sv is null) return;

        var current = e.GetPosition(sv);
        var delta = current - _panStartViewportPoint;
        // 拖动方向与滚动方向相反：向右拖图像，视口看到左侧内容 → 偏移减小
        sv.Offset = new Vector(
            Math.Max(0, _panStartOffset.X - delta.X),
            Math.Max(0, _panStartOffset.Y - delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            UpdateCursor();
            e.Handled = true;
        }
    }

    /// <summary>鼠标滚轮缩放（100%~500%）。有像素数据时滚轮始终用于缩放，不滚动。</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (GridWidth <= 0 || GridHeight <= 0 || PixelData is null) return;

        // 有数据时滚轮始终用于缩放，阻止外层 ScrollViewer 滚动。
        e.Handled = true;

        double delta = e.Delta.Y;
        if (delta == 0) return;

        const double step = 0.25;
        double newZoom = Zoom + (delta > 0 ? step : -step);
        Zoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
    }

    /// <summary>查找祖先 ScrollViewer（用于平移时调整滚动偏移）。</summary>
    private ScrollViewer? FindScrollViewer() => this.FindAncestorOfType<ScrollViewer>();

    /// <summary>内容是否超出视口（决定是否允许右键拖动平移）。</summary>
    private static bool IsOverflow(ScrollViewer sv)
        => sv.Extent.Width > sv.Viewport.Width + 0.5
           || sv.Extent.Height > sv.Viewport.Height + 0.5;

    // 平移状态
    private bool _isPanning;
    private Point _panStartViewportPoint;
    private Vector _panStartOffset;

    private IBrush GetBrush(byte r, byte g, byte b, byte a)
    {
        uint key = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        if (!_brushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            _brushCache[key] = brush;
        }
        return brush;
    }

    /// <summary>
    /// 在指定矩形内绘制 2x2 灰白相间的棋盘格，用于表示被删除（透明）的像素。
    /// 左上、右下为白色；右上、左下为浅灰。
    /// </summary>
    private static void DrawCheckerboard(DrawingContext context, Rect rect)
    {
        double halfW = rect.Width / 2;
        double halfH = rect.Height / 2;
        double x0 = rect.X, y0 = rect.Y;
        double x1 = x0 + halfW, y1 = y0 + halfH;
        context.DrawRectangle(CheckerLightBrush, null, new Rect(x0, y0, halfW, halfH));
        context.DrawRectangle(CheckerDarkBrush, null, new Rect(x1, y0, halfW, halfH));
        context.DrawRectangle(CheckerDarkBrush, null, new Rect(x0, y1, halfW, halfH));
        context.DrawRectangle(CheckerLightBrush, null, new Rect(x1, y1, halfW, halfH));
    }
}
