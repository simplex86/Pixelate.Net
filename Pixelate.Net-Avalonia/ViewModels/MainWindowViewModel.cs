using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixelate.Net;
using Pixelate.Net.Avalonia.Controls;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelate.Net.Avalonia.ViewModels;

/// <summary>
/// 处理模式选项（带中文显示名）。
/// </summary>
public sealed record ProcessModeOption(ProcessMode Value, string DisplayName);

/// <summary>
/// 显示模式选项（带中文显示名）。
/// </summary>
public sealed record DisplayModeOption(DisplayMode Value, string DisplayName);

public partial class MainWindowViewModel : ObservableObject
{
    public ProcessModeOption[] Modes { get; } =
    {
        new(ProcessMode.Realistic, "真实（平均）"),
        new(ProcessMode.Cartoon, "卡通（主色）")
    };

    public DisplayModeOption[] DisplayModes { get; } =
    {
        new(DisplayMode.Square, "方珠"),
        new(DisplayMode.Round, "圆珠")
    };

    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private string? _originalInfo;
    [ObservableProperty] private string? _outputInfo;
    [ObservableProperty] private string? _beadCount;
    [ObservableProperty] private Bitmap? _originalBitmap;
    [ObservableProperty] private byte[]? _pixelatedData;
    [ObservableProperty] private int _pixelatedWidth;
    [ObservableProperty] private int _pixelatedHeight;
    [ObservableProperty] private bool _useManualThreshold;

    private DisplayModeOption _selectedDisplayMode;
    public DisplayModeOption SelectedDisplayMode
    {
        get => _selectedDisplayMode;
        set => SetProperty(ref _selectedDisplayMode, value);
    }

    // NumericUpDown.Value 为 double，用 double 绑定，调用算法时取整。
    private double _horizontalSplits = 50;
    public double HorizontalSplits
    {
        get => _horizontalSplits;
        set => SetProperty(ref _horizontalSplits, Math.Max(1, value));
    }

    private double _colorMergeThreshold = 30;
    public double ColorMergeThreshold
    {
        get => _colorMergeThreshold;
        set => SetProperty(ref _colorMergeThreshold, Math.Max(0, Math.Min(100, value)));
    }

    private ProcessModeOption _selectedMode;
    public ProcessModeOption SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    // 已加载的源像素数据，供反复生成使用。
    private byte[]? _sourceRgba;
    private int _sourceWidth;
    private int _sourceHeight;
    // 最近一次计算的自动阈值，供重置使用。
    private int _autoThreshold = 30;

    public MainWindowViewModel()
    {
        _selectedMode = Modes[1];
        _selectedDisplayMode = DisplayModes[0];
    }

    private bool CanGenerate => _sourceRgba is not null;

    [RelayCommand]
    private void ResetThreshold()
    {
        ColorMergeThreshold = _autoThreshold;
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_sourceRgba is null) return;

        int hs = (int)Math.Round(HorizontalSplits);
        if (hs < 1) hs = 1;
        int threshold = (int)Math.Round(ColorMergeThreshold);
        if (threshold < 0) threshold = 0;
        if (threshold > 100) threshold = 100;
        var mode = _selectedMode?.Value ?? ProcessMode.Realistic;
        var options = new PixelateOptions
        {
            HorizontalSplits = hs,
            ColorMergeThreshold = threshold,
            UseAutoThreshold = !UseManualThreshold,
            Mode = mode
        };

        try
        {
            int ps = Math.Max(1, (int)Math.Ceiling((double)_sourceWidth / hs));
            int outW = (_sourceWidth + ps - 1) / ps;
            int outH = (_sourceHeight + ps - 1) / ps;

            byte[] source = _sourceRgba;
            int sw = _sourceWidth;
            int sh = _sourceHeight;
            byte[] outRgba = await Task.Run(() => ImagePixelator.Pixelate(source, sw, sh, options));

            PixelatedData = outRgba;
            PixelatedWidth = outW;
            PixelatedHeight = outH;

            OutputInfo = $"像素化后分辨率: {outW}×{outH}";
            BeadCount = $"拼豆总数: {outW * outH}";
        }
        catch (Exception)
        {
        }
    }

    [RelayCommand]
    private async Task PickImageAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window is null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files is null || files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        await LoadImageAsync(path);
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            await using var fs = File.OpenRead(path);

            // 原图显示位图。
            Bitmap? oldOriginal = OriginalBitmap;
            OriginalBitmap = new Bitmap(fs);
            oldOriginal?.Dispose();

            fs.Position = 0;
            using var img = await Task.Run(() => SixLabors.ImageSharp.Image.Load<Rgba32>(fs));

            // 取出非预乘 RGBA 字节，行优先，步长 = width*4。
            _sourceWidth = img.Width;
            _sourceHeight = img.Height;
            _sourceRgba = new byte[img.Width * img.Height * 4];
            img.CopyPixelDataTo(_sourceRgba);

            // 计算自动阈值并设为阈值的默认值。
            byte[] src = _sourceRgba;
            int sw = img.Width;
            int sh = img.Height;
            _autoThreshold = await Task.Run(() => ImagePixelator.ComputeAutoThreshold(src, sw, sh));
            ColorMergeThreshold = _autoThreshold;

            ImagePath = path;
            OriginalInfo = $"原图分辨率: {img.Width}×{img.Height}";
            GenerateCommand.NotifyCanExecuteChanged();

            // 加载后立即生成一次，提供即时反馈。
            await GenerateAsync();
        }
        catch (Exception)
        {
        }
    }
}
