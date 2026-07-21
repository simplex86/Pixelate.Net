using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Pixelate.Net.Avalonia.Controls;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Pixelate.Net.Avalonia.Services;

public static class PixelExporter
{
    private const int PixelSize = 20;

    public static async Task ExportAsync(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, string format,
        bool transparentBackground = true,
        int boardSize = 0)
    {
        // 判断是否需要多底板导出：boardSize > 0 且画布超出单张底板。
        bool multiBoard = boardSize > 0 && (width > boardSize || height > boardSize);
        if (!multiBoard)
        {
            switch (format.ToUpperInvariant())
            {
                case "PNG":
                    await ExportImageAsync(rgba, width, height, mode, showCodes, codeMap, path, isJpg: false, transparentBackground);
                    break;
                case "JPG":
                    await ExportImageAsync(rgba, width, height, mode, showCodes, codeMap, path, isJpg: true, transparentBackground: false);
                    break;
                case "SVG":
                    ExportSvg(rgba, width, height, mode, showCodes, codeMap, path, transparentBackground);
                    break;
                case "PDF":
                    ExportPdf(rgba, width, height, mode, showCodes, codeMap, path, transparentBackground, boardSize);
                    break;
            }
            return;
        }

        // 多底板导出
        switch (format.ToUpperInvariant())
        {
            case "PNG":
            case "JPG":
                await ExportMultiBoardImageAsync(rgba, width, height, mode, showCodes, codeMap, path, format, transparentBackground, boardSize);
                break;
            case "SVG":
                ExportMultiBoardSvg(rgba, width, height, mode, showCodes, codeMap, path, transparentBackground, boardSize);
                break;
            case "PDF":
                ExportPdf(rgba, width, height, mode, showCodes, codeMap, path, transparentBackground, boardSize);
                break;
        }
    }

    /// <summary>
    /// 渲染像素化结果到 ImageSharp 图像。
    /// transparentBackground=true 时使用 Rgba32 透明背景（被删除像素保持透明）；
    /// transparentBackground=false 时使用 Rgb24 白色背景（被删除像素呈现白色）。
    /// </summary>
    public static Image RenderImage(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        bool transparentBackground)
    {
        int imgW = width * PixelSize;
        int imgH = height * PixelSize;
        Image image = transparentBackground
            ? new Image<Rgba32>(imgW, imgH, Color.Transparent)
            : new Image<Rgb24>(imgW, imgH, Color.White);

        var font = SystemFonts.CreateFont("Arial", (int)(PixelSize / 2.5), FontStyle.Regular);

        image.Mutate(ctx =>
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    if (rgba[i + 3] == 0) continue; // 跳过已删除像素
                    byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                    var color = Color.FromRgb(r, g, b);
                    int px = x * PixelSize;
                    int py = y * PixelSize;

                    if (mode == DisplayMode.Square)
                    {
                        ctx.Fill(color, new RectangularPolygon(px, py, PixelSize, PixelSize));
                    }
                    else if (mode == DisplayMode.Round)
                    {
                        ctx.Fill(color, new EllipsePolygon(px + PixelSize / 2f, py + PixelSize / 2f, PixelSize / 2f));
                    }
                    else // Hollow
                    {
                        float sw = Math.Max(1f, PixelSize / 6f);
                        ctx.Draw(color, sw, new EllipsePolygon(px + PixelSize / 2f, py + PixelSize / 2f, PixelSize / 2f - sw / 2));
                    }
                }
            }

            if (showCodes && codeMap is not null)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width + x) * 4;
                        if (rgba[i + 3] == 0) continue; // 跳过已删除像素
                        byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                        uint key = ((uint)r << 16) | ((uint)g << 8) | b;
                        if (!codeMap.TryGetValue(key, out var code))
                            continue;

                        double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                        var textColor = brightness > 128 ? Color.Black : Color.White;
                        float px = x * PixelSize + PixelSize / 2f;
                        float py = y * PixelSize + PixelSize / 2f;
                        var size = TextMeasurer.MeasureSize(code, new TextOptions(font));
                        ctx.DrawText(code, font, textColor, new PointF(px - size.Width / 2, py - size.Height / 2));
                    }
                }
            }
        });

        return image;
    }

    /// <summary>从画布中提取单张底板区域的像素数据。</summary>
    private static byte[] ExtractBoard(byte[] rgba, int canvasW, int canvasH, int bx, int by, int boardSize)
    {
        int x0 = bx * boardSize;
        int y0 = by * boardSize;
        int regionW = Math.Min(boardSize, canvasW - x0);
        int regionH = Math.Min(boardSize, canvasH - y0);
        byte[] board = new byte[boardSize * boardSize * 4]; // 不足部分默认透明
        for (int y = 0; y < regionH; y++)
        {
            int srcRow = ((y0 + y) * canvasW + x0) * 4;
            int dstRow = y * boardSize * 4;
            Buffer.BlockCopy(rgba, srcRow, board, dstRow, regionW * 4);
        }
        return board;
    }

    /// <summary>计算底板布局（水平与竖直方向的底板数）。</summary>
    private static (int boardsX, int boardsY) GetBoardLayout(int width, int height, int boardSize)
    {
        if (boardSize <= 0) return (1, 1);
        int bx = Math.Max(1, (int)Math.Ceiling(width / (double)boardSize));
        int by = Math.Max(1, (int)Math.Ceiling(height / (double)boardSize));
        return (bx, by);
    }

    /// <summary>根据基础路径与底板索引生成单张底板文件路径。</summary>
    private static string GetBoardPath(string basePath, string ext, int bx, int by, int totalX, int totalY)
    {
        string dir = System.IO.Path.GetDirectoryName(basePath) ?? string.Empty;
        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(basePath);
        string suffix = totalX == 1 && totalY == 1
            ? string.Empty
            : $"_{bx + 1}x{by + 1}";
        string fileName = $"{nameNoExt}{suffix}.{ext}";
        return string.IsNullOrEmpty(dir) ? fileName : System.IO.Path.Combine(dir, fileName);
    }

    private static async Task ExportImageAsync(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, bool isJpg, bool transparentBackground)
    {
        using var image = RenderImage(rgba, width, height, mode, showCodes, codeMap, transparentBackground);

        await using var fs = File.Create(path);
        if (isJpg)
            await image.SaveAsJpegAsync(fs);
        else
            await image.SaveAsPngAsync(fs);
    }

    private static async Task ExportMultiBoardImageAsync(
        byte[] rgba, int canvasW, int canvasH,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, string format, bool transparentBackground, int boardSize)
    {
        var (bx, by) = GetBoardLayout(canvasW, canvasH, boardSize);
        string ext = format.ToUpperInvariant() == "JPG" ? "jpg" : "png";
        bool isJpg = format.ToUpperInvariant() == "JPG";
        // JPG 不支持透明，单底板背景为白
        bool boardTransparent = isJpg ? false : transparentBackground;

        for (int y = 0; y < by; y++)
        {
            for (int x = 0; x < bx; x++)
            {
                byte[] board = ExtractBoard(rgba, canvasW, canvasH, x, y, boardSize);
                string boardPath = GetBoardPath(path, ext, x, y, bx, by);
                using var image = RenderImage(board, boardSize, boardSize, mode, showCodes, codeMap, boardTransparent);
                await using var fs = File.Create(boardPath);
                if (isJpg)
                    await image.SaveAsJpegAsync(fs);
                else
                    await image.SaveAsPngAsync(fs);
            }
        }
    }

    private static void ExportSvg(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, bool transparentBackground)
    {
        int imgW = width * PixelSize;
        int imgH = height * PixelSize;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{imgW}\" height=\"{imgH}\">");
        if (!transparentBackground)
        {
            sb.Append($"<rect width=\"{imgW}\" height=\"{imgH}\" fill=\"white\"/>");
        }

        double half = PixelSize / 2.0;
        double sw = Math.Max(1, PixelSize / 6.0);
        double fontSize = PixelSize / 2.5;

        AppendSvgPixels(sb, rgba, width, height, mode, half, sw);
        if (showCodes && codeMap is not null)
            AppendSvgCodes(sb, rgba, width, height, codeMap, half, fontSize);

        sb.Append("</svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void ExportMultiBoardSvg(
        byte[] rgba, int canvasW, int canvasH,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, bool transparentBackground, int boardSize)
    {
        var (bx, by) = GetBoardLayout(canvasW, canvasH, boardSize);
        for (int y = 0; y < by; y++)
        {
            for (int x = 0; x < bx; x++)
            {
                byte[] board = ExtractBoard(rgba, canvasW, canvasH, x, y, boardSize);
                string boardPath = GetBoardPath(path, "svg", x, y, bx, by);
                ExportSvg(board, boardSize, boardSize, mode, showCodes, codeMap, boardPath, transparentBackground);
            }
        }
    }

    private static void AppendSvgPixels(StringBuilder sb, byte[] rgba, int width, int height, DisplayMode mode, double half, double sw)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (rgba[i + 3] == 0) continue;
                byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                string hex = $"#{r:X2}{g:X2}{b:X2}";
                double cx = x * PixelSize + half;
                double cy = y * PixelSize + half;

                if (mode == DisplayMode.Square)
                {
                    sb.Append($"<rect x=\"{x * PixelSize}\" y=\"{y * PixelSize}\" width=\"{PixelSize}\" height=\"{PixelSize}\" fill=\"{hex}\"/>");
                }
                else if (mode == DisplayMode.Round)
                {
                    sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{half}\" fill=\"{hex}\"/>");
                }
                else // Hollow
                {
                    sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{half - sw / 2}\" fill=\"none\" stroke=\"{hex}\" stroke-width=\"{sw}\"/>");
                }
            }
        }
    }

    private static void AppendSvgCodes(StringBuilder sb, byte[] rgba, int width, int height, IReadOnlyDictionary<uint, string> codeMap, double half, double fontSize)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (rgba[i + 3] == 0) continue;
                byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                uint key = ((uint)r << 16) | ((uint)g << 8) | b;
                if (!codeMap.TryGetValue(key, out var code))
                    continue;
                double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                string textColor = brightness > 128 ? "black" : "white";
                double cx = x * PixelSize + half;
                double cy = y * PixelSize + half;
                sb.Append($"<text x=\"{cx}\" y=\"{cy}\" text-anchor=\"middle\" dominant-baseline=\"central\" font-size=\"{fontSize}\" fill=\"{textColor}\" font-family=\"Arial\">{code}</text>");
            }
        }
    }

    private static void ExportPdf(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, bool transparentBackground, int boardSize)
    {
        var doc = new PdfDocument();
        var (bx, by) = boardSize > 0 ? GetBoardLayout(width, height, boardSize) : (1, 1);
        bool multiBoard = bx > 1 || by > 1;

        double half = PixelSize / 2.0;
        double sw = Math.Max(1, PixelSize / 6.0);
        var font = new XFont("Arial", PixelSize / 2.5);

        if (!multiBoard)
        {
            var page = doc.AddPage();
            page.Width = new XUnit(width * PixelSize);
            page.Height = new XUnit(height * PixelSize);
            var gfx = XGraphics.FromPdfPage(page);
            RenderPdfPage(gfx, rgba, width, height, mode, showCodes, codeMap, transparentBackground, half, sw, font);
        }
        else
        {
            for (int yb = 0; yb < by; yb++)
            {
                for (int xb = 0; xb < bx; xb++)
                {
                    byte[] board = ExtractBoard(rgba, width, height, xb, yb, boardSize);
                    var page = doc.AddPage();
                    page.Width = new XUnit(boardSize * PixelSize);
                    page.Height = new XUnit(boardSize * PixelSize);
                    var gfx = XGraphics.FromPdfPage(page);
                    RenderPdfPage(gfx, board, boardSize, boardSize, mode, showCodes, codeMap, transparentBackground, half, sw, font);
                }
            }
        }

        doc.Save(path);
    }

    private static void RenderPdfPage(
        XGraphics gfx, byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        bool transparentBackground, double half, double sw, XFont font)
    {
        if (!transparentBackground)
        {
            gfx.DrawRectangle(XBrushes.White, 0, 0, width * PixelSize, height * PixelSize);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (rgba[i + 3] == 0) continue;
                byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                var xcolor = XColor.FromArgb(r, g, b);
                double px = x * PixelSize;
                double py = y * PixelSize;
                double cx = px + half;
                double cy = py + half;

                if (mode == DisplayMode.Square)
                {
                    gfx.DrawRectangle(new XSolidBrush(xcolor), px, py, PixelSize, PixelSize);
                }
                else if (mode == DisplayMode.Round)
                {
                    gfx.DrawEllipse(new XSolidBrush(xcolor), px, py, PixelSize, PixelSize);
                }
                else // Hollow
                {
                    double d = PixelSize - sw;
                    gfx.DrawEllipse(new XPen(xcolor, sw), px + sw / 2, py + sw / 2, d, d);
                }
            }
        }

        if (showCodes && codeMap is not null)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    if (rgba[i + 3] == 0) continue;
                    byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                    uint key = ((uint)r << 16) | ((uint)g << 8) | b;
                    if (!codeMap.TryGetValue(key, out var code))
                        continue;
                    double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                    var textColor = brightness > 128 ? XBrushes.Black : XBrushes.White;
                    double cx = x * PixelSize + half;
                    double cy = y * PixelSize + half;
                    gfx.DrawString(code, font, textColor, cx, cy, XStringFormats.Center);
                }
            }
        }
    }
}
