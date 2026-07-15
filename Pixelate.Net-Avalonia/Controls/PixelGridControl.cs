using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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

    /// <summary>像素被点击时触发（仅在 IsEditable=true 时）。</summary>
    public event EventHandler<PixelClickedEventArgs>? PixelClicked;

    static PixelGridControl()
    {
        AffectsRender<PixelGridControl>(PixelDataProperty, GridWidthProperty, GridHeightProperty, DisplayModeProperty, ShowCodesProperty, ColorCodeMapProperty);
        IsEditableProperty.Changed.AddClassHandler<PixelGridControl>((c, e) => c.UpdateCursor());
        IsEyedroppingProperty.Changed.AddClassHandler<PixelGridControl>((c, e) => c.UpdateCursor());
    }

    // 颜色 → 画刷缓存，避免每帧重复创建大量 SolidColorBrush。
    private readonly Dictionary<uint, IBrush> _brushCache = new();
    private static readonly IBrush GridBrush = new SolidColorBrush(0x88888888);
    private static readonly IPen GridPen = new Pen(GridBrush, 1);
    // 被删除像素（alpha=0）的显示颜色
    private static readonly IBrush DeletedBrush = new SolidColorBrush(0xFFFFFFFF);

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
                // 绘制色块
                for (int y = 0; y < GridHeight; y++)
                {
                    for (int x = 0; x < GridWidth; x++)
                    {
                        int i = (y * GridWidth + x) * 4;
                        var brush = data[i + 3] == 0 ? DeletedBrush : GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                        var rect = new Rect(x * cw, y * ch, cw, ch);
                        context.DrawRectangle(brush, null, rect);
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
                        var brush = data[i + 3] == 0 ? DeletedBrush : GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                        var rect = new Rect(x * cw, y * ch, cw, ch);
                        context.DrawEllipse(brush, null, rect);
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
                        // 被删除的像素不绘制空环
                        if (data[i + 3] == 0) continue;
                        var brush = GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                        var rect = new Rect(x * cw + inset, y * ch + inset, cw - strokeWidth, ch - strokeWidth);
                        context.DrawEllipse(null, new Pen(brush, strokeWidth), rect);
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsEditable) return;

        var point = e.GetPosition(this);
        if (PointToPixel(point) is (int px, int py))
        {
            e.Handled = true;
            PixelClicked?.Invoke(this, new PixelClickedEventArgs(px, py));
        }
    }

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
}
