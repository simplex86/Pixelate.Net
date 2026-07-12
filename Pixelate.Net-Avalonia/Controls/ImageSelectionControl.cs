using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Pixelate.Net.Avalonia.Controls;

/// <summary>
/// 显示图片并支持框选局部区域的自定义控件。
/// 选择框使用归一化坐标（0~1），与显示尺寸无关。
/// 编辑模式下显示蒙版（压暗非选中区域）、选择框边框和拖拽手柄。
/// </summary>
public class ImageSelectionControl : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageSelectionControl, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<Rect> SelectionProperty =
        AvaloniaProperty.Register<ImageSelectionControl, Rect>(nameof(Selection), defaultValue: new Rect(0, 0, 1, 1));

    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<ImageSelectionControl, bool>(nameof(IsEditing));

    private const double HandleSize = 8;
    private const double MinSelectionSize = 0.05;

    private enum DragMode { None, Move, Resize }
    private DragMode _dragMode = DragMode.None;
    private bool _resizeLeft, _resizeRight, _resizeTop, _resizeBottom;
    private Point _lastPoint;

    static ImageSelectionControl()
    {
        AffectsRender<ImageSelectionControl>(SourceProperty, SelectionProperty, IsEditingProperty);
    }

    public ImageSelectionControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>归一化选择框 (0~1)</summary>
    public Rect Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var src = Source;
        if (src == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var imgRect = GetImageRect();
        if (imgRect.Width <= 0 || imgRect.Height <= 0) return;

        // 绘制图片
        context.DrawImage(src, new Rect(0, 0, src.Size.Width, src.Size.Height), imgRect);

        var selRect = SelectionToScreen(imgRect, Selection);

        // 始终绘制蒙版（压暗非选中区域），让用户清晰看到选择的部分
        var dimBrush = new SolidColorBrush(0x80000000);
        // 上
        context.DrawRectangle(dimBrush, null, new Rect(imgRect.X, imgRect.Y, imgRect.Width, selRect.Y - imgRect.Y));
        // 下
        context.DrawRectangle(dimBrush, null, new Rect(imgRect.X, selRect.Bottom, imgRect.Width, imgRect.Bottom - selRect.Bottom));
        // 左
        context.DrawRectangle(dimBrush, null, new Rect(imgRect.X, selRect.Y, selRect.X - imgRect.X, selRect.Height));
        // 右
        context.DrawRectangle(dimBrush, null, new Rect(selRect.Right, selRect.Y, imgRect.Right - selRect.Right, selRect.Height));

        if (!IsEditing) return;

        // 绘制选择框边框
        context.DrawRectangle(null, new Pen(Brushes.White, 1), selRect);

        // 绘制手柄（四角 + 四边中点）
        DrawHandle(context, selRect.TopLeft);
        DrawHandle(context, selRect.TopRight);
        DrawHandle(context, selRect.BottomLeft);
        DrawHandle(context, selRect.BottomRight);
        DrawHandle(context, new Point(selRect.X + selRect.Width / 2, selRect.Y));
        DrawHandle(context, new Point(selRect.X + selRect.Width / 2, selRect.Bottom));
        DrawHandle(context, new Point(selRect.X, selRect.Y + selRect.Height / 2));
        DrawHandle(context, new Point(selRect.Right, selRect.Y + selRect.Height / 2));
    }

    private void DrawHandle(DrawingContext context, Point center)
    {
        double hs = HandleSize / 2;
        var rect = new Rect(center.X - hs, center.Y - hs, HandleSize, HandleSize);
        context.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), rect);
    }

    private Rect GetImageRect()
    {
        var src = Source;
        if (src == null) return default;

        double imgW = src.Size.Width;
        double imgH = src.Size.Height;
        if (imgW <= 0 || imgH <= 0) return default;

        double scaleX = Bounds.Width / imgW;
        double scaleY = Bounds.Height / imgH;
        double scale = Math.Min(scaleX, scaleY);

        double drawW = imgW * scale;
        double drawH = imgH * scale;
        double offsetX = (Bounds.Width - drawW) / 2;
        double offsetY = (Bounds.Height - drawH) / 2;

        return new Rect(offsetX, offsetY, drawW, drawH);
    }

    private Rect SelectionToScreen(Rect imgRect, Rect sel)
    {
        return new Rect(
            imgRect.X + sel.X * imgRect.Width,
            imgRect.Y + sel.Y * imgRect.Height,
            sel.Width * imgRect.Width,
            sel.Height * imgRect.Height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsEditing) return;

        var point = e.GetPosition(this);
        var imgRect = GetImageRect();
        var selScreen = SelectionToScreen(imgRect, Selection);

        DetermineDragMode(point, selScreen);

        if (_dragMode != DragMode.None)
        {
            _lastPoint = point;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void DetermineDragMode(Point pos, Rect selScreen)
    {
        _resizeLeft = _resizeRight = _resizeTop = _resizeBottom = false;

        bool nearLeft = Math.Abs(pos.X - selScreen.Left) <= HandleSize;
        bool nearRight = Math.Abs(pos.X - selScreen.Right) <= HandleSize;
        bool nearTop = Math.Abs(pos.Y - selScreen.Top) <= HandleSize;
        bool nearBottom = Math.Abs(pos.Y - selScreen.Bottom) <= HandleSize;

        bool inVertRange = pos.Y >= selScreen.Top - HandleSize && pos.Y <= selScreen.Bottom + HandleSize;
        bool inHorzRange = pos.X >= selScreen.Left - HandleSize && pos.X <= selScreen.Right + HandleSize;

        if (nearLeft && inVertRange) _resizeLeft = true;
        if (nearRight && inVertRange) _resizeRight = true;
        if (nearTop && inHorzRange) _resizeTop = true;
        if (nearBottom && inHorzRange) _resizeBottom = true;

        if (_resizeLeft || _resizeRight || _resizeTop || _resizeBottom)
        {
            _dragMode = DragMode.Resize;
        }
        else if (pos.X >= selScreen.Left && pos.X <= selScreen.Right &&
                 pos.Y >= selScreen.Top && pos.Y <= selScreen.Bottom)
        {
            _dragMode = DragMode.Move;
        }
        else
        {
            _dragMode = DragMode.None;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!IsEditing || _dragMode == DragMode.None) return;

        var point = e.GetPosition(this);
        var imgRect = GetImageRect();
        if (imgRect.Width <= 0 || imgRect.Height <= 0) return;

        double dx = (point.X - _lastPoint.X) / imgRect.Width;
        double dy = (point.Y - _lastPoint.Y) / imgRect.Height;

        var sel = Selection;

        if (_dragMode == DragMode.Move)
        {
            double newX = Math.Max(0, Math.Min(1 - sel.Width, sel.X + dx));
            double newY = Math.Max(0, Math.Min(1 - sel.Height, sel.Y + dy));
            Selection = new Rect(newX, newY, sel.Width, sel.Height);
        }
        else if (_dragMode == DragMode.Resize)
        {
            double x = sel.X, y = sel.Y, w = sel.Width, h = sel.Height;

            if (_resizeLeft)
            {
                double newX = Math.Max(0, Math.Min(x + w - MinSelectionSize, x + dx));
                w = w - (newX - x);
                x = newX;
            }
            if (_resizeRight)
            {
                w = Math.Max(MinSelectionSize, Math.Min(1 - x, w + dx));
            }
            if (_resizeTop)
            {
                double newY = Math.Max(0, Math.Min(y + h - MinSelectionSize, y + dy));
                h = h - (newY - y);
                y = newY;
            }
            if (_resizeBottom)
            {
                h = Math.Max(MinSelectionSize, Math.Min(1 - y, h + dy));
            }

            Selection = new Rect(x, y, w, h);
        }

        _lastPoint = point;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            _resizeLeft = _resizeRight = _resizeTop = _resizeBottom = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
