using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Controls;
using SixLabors.ImageSharp;
using Pixelate.Net.Avalonia.Controls;

namespace Pixelate.Net.Avalonia.Services;

/// <summary>
/// 打印像素化结果。
/// Windows 平台调用系统打印对话框（Win32 PrintDlg）；
/// 其他平台导出 PDF 临时文件并用系统默认查看器打开（由用户在查看器中触发打印）。
/// </summary>
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
    /// 渲染像素化结果并启动打印流程。
    /// Windows 上弹出系统打印对话框；其他平台导出 PDF 临时文件并用系统默认查看器打开。
    /// </summary>
    /// <param name="owner">主窗口引用，非 Windows 平台用于调用 Launcher；可省略。</param>
    /// <param name="boardSize">单张底板边长；&gt;0 时按底板分割多页打印。</param>
    public static async Task PrintAsync(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string documentName,
        Window? owner = null,
        int boardSize = 0)
    {
        if (OperatingSystem.IsWindows())
        {
            await PrintWindowsAsync(rgba, width, height, mode, showCodes, codeMap, documentName, boardSize);
        }
        else
        {
            await PrintViaPdfLauncherAsync(rgba, width, height, mode, showCodes, codeMap, documentName, owner, boardSize);
        }
    }

    /// <summary>Windows 平台：渲染为 PNG，通过 Win32 PrintDlg 弹出系统打印对话框打印。</summary>
    [SupportedOSPlatform("windows")]
    private static async Task PrintWindowsAsync(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string documentName,
        int boardSize)
    {
        // 在后台线程渲染 PNG 字节，避免阻塞 UI
        // 打印时使用白底渲染，被删除（透明）像素打印成白色
        byte[] pngBytes = await Task.Run(() =>
        {
            using var image = PixelExporter.RenderImage(rgba, width, height, mode, showCodes, codeMap, transparentBackground: false);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        });

        // 加载到 System.Drawing.Bitmap 并弹出系统打印对话框
        using var bitmap = new Bitmap(new MemoryStream(pngBytes));
        PrintBitmap(bitmap, documentName);
    }

    /// <summary>非 Windows 平台：导出 PDF 临时文件并用系统默认查看器打开，由用户在查看器中触发打印。</summary>
    private static async Task PrintViaPdfLauncherAsync(
        byte[] rgba, int width, int height,
        DisplayMode mode, bool showCodes,
        IReadOnlyDictionary<uint, string>? codeMap,
        string documentName,
        Window? owner,
        int boardSize)
    {
        // 生成唯一临时文件路径
        string tempPath = Path.Combine(Path.GetTempPath(), $"{documentName}_{Guid.NewGuid():N}.pdf");
        try
        {
            // 打印时使用白底渲染，被删除（透明）像素打印成白色
            await PixelExporter.ExportAsync(
                rgba, width, height, mode, showCodes, codeMap, tempPath, "PDF",
                transparentBackground: false, boardSize: boardSize);

            if (owner is null)
                return;

            var storageFile = await owner.StorageProvider.TryGetFileFromPathAsync(new Uri(tempPath));
            if (storageFile is not null)
                await owner.Launcher.LaunchFileAsync(storageFile);
        }
        catch
        {
            // 临时文件由系统清理策略处理，不在此删除以避免占用
        }
    }

    [SupportedOSPlatform("windows")]
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
