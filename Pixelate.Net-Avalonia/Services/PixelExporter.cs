using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Fonts;
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
        bool transparentBackground = true)
    {
        switch (format.ToUpperInvariant())
        {
            case "PNG":
                await ExportImageAsync(rgba, width, height, mode, showCodes, codeMap, path, isJpg: false, transparentBackground);
                break;
            case "JPG":
                // JPG 不支持 alpha 通道，被删除像素以白色呈现
                await ExportImageAsync(rgba, width, height, mode, showCodes, codeMap, path, isJpg: true, transparentBackground: false);
                break;
            case "SVG":
                ExportSvg(rgba, width, height, mode, showCodes, codeMap, path, transparentBackground);
                break;
            case "PDF":
                ExportPdf(rgba, width, height, mode, showCodes, codeMap, path, transparentBackground);
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
            // 非透明背景：绘制白色底，被删除像素呈现白色
            sb.Append($"<rect width=\"{imgW}\" height=\"{imgH}\" fill=\"white\"/>");
        }

        double half = PixelSize / 2.0;
        double sw = Math.Max(1, PixelSize / 6.0);
        double fontSize = PixelSize / 2.5;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (rgba[i + 3] == 0) continue; // 跳过已删除像素
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
                    string textColor = brightness > 128 ? "black" : "white";
                    double cx = x * PixelSize + half;
                    double cy = y * PixelSize + half;
                    sb.Append($"<text x=\"{cx}\" y=\"{cy}\" text-anchor=\"middle\" dominant-baseline=\"central\" font-size=\"{fontSize}\" fill=\"{textColor}\" font-family=\"Arial\">{code}</text>");
                }
            }
        }

        sb.Append("</svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void ExportPdf(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string path, bool transparentBackground)
    {
        var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = new XUnit(width * PixelSize);
        page.Height = new XUnit(height * PixelSize);
        var gfx = XGraphics.FromPdfPage(page);

        if (!transparentBackground)
        {
            // 非透明背景：绘制白色底，被删除像素呈现白色
            gfx.DrawRectangle(XBrushes.White, 0, 0, width * PixelSize, height * PixelSize);
        }

        double half = PixelSize / 2.0;
        double sw = Math.Max(1, PixelSize / 6.0);
        var font = new XFont("Arial", PixelSize / 2.5);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (rgba[i + 3] == 0) continue; // 跳过已删除像素
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
                    if (rgba[i + 3] == 0) continue; // 跳过已删除像素
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

        doc.Save(path);
    }
}
