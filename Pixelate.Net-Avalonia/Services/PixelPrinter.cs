using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Pixelate.Net.Avalonia.Controls;

namespace Pixelate.Net.Avalonia.Services;

/// <summary>
/// 调用 Win32 系统打印对话框打印像素化结果。
/// 仅适用于 Windows 平台（依赖 comdlg32 与 System.Drawing.Printing）。
/// </summary>
[SupportedOSPlatform("windows")]
public static class PixelPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PRINTDLG
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hDevMode;
        public IntPtr hDevNames;
        public IntPtr hdc;
        public uint Flags;
        public ushort nFromPage;
        public ushort nToPage;
        public ushort nMinPage;
        public ushort nMaxPage;
        public ushort nCopies;
        public IntPtr hInstance;
        public IntPtr lCustData;
        public IntPtr lpfnPrintHook;
        public IntPtr lpfnSetupHook;
        public string lpPrintTemplateName;
        public string lpSetupTemplateName;
        public IntPtr hPrintTemplate;
        public IntPtr hSetupTemplate;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintDlg(ref PRINTDLG lppd);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>
    /// 渲染像素化结果并弹出系统打印对话框打印。
    /// 遵循 ShowCodes 设置决定是否打印颜色编码。
    /// </summary>
    public static async Task PrintAsync(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string documentName)
    {
        // 在后台线程渲染 PNG 字节，避免阻塞 UI
        byte[] pngBytes = await Task.Run(() =>
        {
            using var image = PixelExporter.RenderImage(rgba, width, height, mode, showCodes, codeMap);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        });

        // 加载到 System.Drawing.Bitmap 并弹出系统打印对话框
        using var bitmap = new Bitmap(new MemoryStream(pngBytes));
        PrintBitmap(bitmap, documentName);
    }

    private static void PrintBitmap(Bitmap bitmap, string documentName)
    {
        var settings = new PrinterSettings();
        var pageSettings = new PageSettings(settings);

        // 从默认打印机设置获取 DEVMODE / DEVNAMES 句柄
        IntPtr hDevModeInput = settings.GetHdevmode(pageSettings);
        IntPtr hDevNamesInput = settings.GetHdevnames();

        var pd = new PRINTDLG
        {
            lStructSize = Marshal.SizeOf<PRINTDLG>(),
            hwndOwner = IntPtr.Zero,
            hDevMode = hDevModeInput,
            hDevNames = hDevNamesInput,
            Flags = 0
        };

        try
        {
            // 弹出系统打印对话框；用户取消时返回 false
            if (!PrintDlg(ref pd))
                return;

            // 将对话框返回的设置应用到 PrinterSettings
            if (pd.hDevMode != IntPtr.Zero)
                settings.SetHdevmode(pd.hDevMode);
            if (pd.hDevNames != IntPtr.Zero)
                settings.SetHdevnames(pd.hDevNames);

            var doc = new PrintDocument
            {
                PrinterSettings = settings,
                DocumentName = documentName
            };

            doc.PrintPage += (_, e) =>
            {
                var g = e.Graphics;
                if (g is null) return;
                var bounds = e.MarginBounds;
                // 等比缩放并居中绘制
                float scale = Math.Min(
                    (float)bounds.Width / bitmap.Width,
                    (float)bounds.Height / bitmap.Height);
                float w = bitmap.Width * scale;
                float h = bitmap.Height * scale;
                float x = bounds.Left + ((float)bounds.Width - w) / 2;
                float y = bounds.Top + ((float)bounds.Height - h) / 2;
                g.DrawImage(bitmap, x, y, w, h);
                e.HasMorePages = false;
            };

            doc.Print();
        }
        finally
        {
            // 释放输入和返回的句柄
            if (hDevModeInput != IntPtr.Zero) GlobalFree(hDevModeInput);
            if (hDevNamesInput != IntPtr.Zero) GlobalFree(hDevNamesInput);
            if (pd.hDevMode != IntPtr.Zero) GlobalFree(pd.hDevMode);
            if (pd.hDevNames != IntPtr.Zero) GlobalFree(pd.hDevNames);
        }
    }
}
