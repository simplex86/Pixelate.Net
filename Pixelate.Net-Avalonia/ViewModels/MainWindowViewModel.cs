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

    /// <summary>选择框（归一化坐标 0~1），与控件双向绑定。</summary>
    [ObservableProperty] private Rect _selection = new(0, 0, 1, 1);

    /// <summary>是否处于编辑选择框模式。</summary>
    [ObservableProperty] private bool _isEditing;

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
    // 已应用的选择框（用于实际像素化），与编辑中的 Selection 分离。
    private Rect _appliedSelection = new(0, 0, 1, 1);
    // 标记是否需要重新计算自动阈值（图像加载或裁剪变化时）。
    private bool _needsThresholdUpdate = true;

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

    /// <summary>进入编辑模式，从已应用的选择框开始调整。</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void Edit()
    {
        Selection = _appliedSelection;
        IsEditing = true;
    }

    /// <summary>取消编辑，还原为上一次应用的选择框。</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void Cancel()
    {
        Selection = _appliedSelection;
        IsEditing = false;
    }

    /// <summary>应用当前选择框，重新生成像素化结果。</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task ApplyAsync()
    {
        _appliedSelection = Selection;
        IsEditing = false;
        _needsThresholdUpdate = true;
        await GenerateAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_sourceRgba is null) return;

        var (cropData, cropW, cropH) = ExtractCropData();
        if (cropW <= 0 || cropH <= 0) return;

        // 若需要重算自动阈值（裁剪变化时），无论手动/自动模式都更新 _autoThreshold，
        // 以便 Reset 按钮能重置到当前裁剪区域对应的自动阈值；
        // 仅在自动模式下同步更新控件显示值。
        if (_needsThresholdUpdate)
        {
            byte[] cd = cropData;
            _autoThreshold = await Task.Run(() => ImagePixelator.ComputeAutoThreshold(cd, cropW, cropH));
            if (!UseManualThreshold)
            {
                ColorMergeThreshold = _autoThreshold;
            }
            _needsThresholdUpdate = false;
        }

        int hs = (int)Math.Round(HorizontalSplits);
        if (hs < 1) hs = 1;
        int threshold = UseManualThreshold ? (int)Math.Round(ColorMergeThreshold) : _autoThreshold;
        if (threshold < 0) threshold = 0;
        if (threshold > 100) threshold = 100;
        var mode = _selectedMode?.Value ?? ProcessMode.Realistic;

        var options = new PixelateOptions
        {
            HorizontalSplits = hs,
            ColorMergeThreshold = threshold,
            Mode = mode
        };

        try
        {
            int ps = Math.Max(1, (int)Math.Ceiling((double)cropW / hs));
            int outW = (cropW + ps - 1) / ps;
            int outH = (cropH + ps - 1) / ps;

            byte[] outRgba = await Task.Run(() => ImagePixelator.Pixelate(cropData, cropW, cropH, options));

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

    /// <summary>根据已应用的选择框从源图像中提取裁剪数据。</summary>
    private (byte[] data, int width, int height) ExtractCropData()
    {
        if (_sourceRgba is null) return (Array.Empty<byte>(), 0, 0);

        int cropX = (int)(_appliedSelection.X * _sourceWidth);
        int cropY = (int)(_appliedSelection.Y * _sourceHeight);
        int cropW = (int)(_appliedSelection.Width * _sourceWidth);
        int cropH = (int)(_appliedSelection.Height * _sourceHeight);

        // 边界保护
        cropX = Math.Clamp(cropX, 0, _sourceWidth - 1);
        cropY = Math.Clamp(cropY, 0, _sourceHeight - 1);
        cropW = Math.Clamp(cropW, 1, _sourceWidth - cropX);
        cropH = Math.Clamp(cropH, 1, _sourceHeight - cropY);

        byte[] cropData = new byte[cropW * cropH * 4];
        int rowBytes = cropW * 4;
        for (int y = 0; y < cropH; y++)
        {
            int srcOffset = ((cropY + y) * _sourceWidth + cropX) * 4;
            int dstOffset = y * rowBytes;
            Array.Copy(_sourceRgba, srcOffset, cropData, dstOffset, rowBytes);
        }

        return (cropData, cropW, cropH);
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

            // 新图片默认框选全部
            Selection = new Rect(0, 0, 1, 1);
            _appliedSelection = new Rect(0, 0, 1, 1);
            IsEditing = false;

            // 计算自动阈值并设为阈值的默认值。
            byte[] src = _sourceRgba;
            int sw = img.Width;
            int sh = img.Height;
            _autoThreshold = await Task.Run(() => ImagePixelator.ComputeAutoThreshold(src, sw, sh));
            ColorMergeThreshold = _autoThreshold;
            _needsThresholdUpdate = false;

            ImagePath = path;
            OriginalInfo = $"原图分辨率: {img.Width}×{img.Height}";
            GenerateCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
            EditCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();

            // 加载后立即生成一次，提供即时反馈。
            await GenerateAsync();
        }
        catch (Exception)
        {
        }
    }
}
