using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
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
    Round
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

    static PixelGridControl()
    {
        AffectsRender<PixelGridControl>(PixelDataProperty, GridWidthProperty, GridHeightProperty, DisplayModeProperty);
    }

    // 颜色 → 画刷缓存，避免每帧重复创建大量 SolidColorBrush。
    private readonly Dictionary<uint, IBrush> _brushCache = new();
    private static readonly IBrush GridBrush = new SolidColorBrush(0x88888888);
    private static readonly IPen GridPen = new Pen(GridBrush, 1);

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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var data = PixelData;
        if (data == null || GridWidth <= 0 || GridHeight <= 0)
            return;
        if (data.Length < GridWidth * GridHeight * 4)
            return;

        double boundsW = Bounds.Width;
        double boundsH = Bounds.Height;
        if (boundsW <= 0 || boundsH <= 0) return;

        // 保持纵横比，居中绘制。
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
                        var brush = GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
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
            else // 圆珠
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    for (int x = 0; x < GridWidth; x++)
                    {
                        int i = (y * GridWidth + x) * 4;
                        var brush = GetBrush(data[i], data[i + 1], data[i + 2], data[i + 3]);
                        var rect = new Rect(x * cw, y * ch, cw, ch);
                        context.DrawEllipse(brush, null, rect);
                    }
                }
            }
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
