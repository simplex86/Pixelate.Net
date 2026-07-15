using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixelate.Net;
using Pixelate.Net.Avalonia.Controls;
using Pixelate.Net.Avalonia.Services;
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

/// <summary>
/// 拼豆品牌色卡选项（带中文显示名）。
/// </summary>
public sealed record BeadBrandOption(BeadBrand Value, string DisplayName);

/// <summary>
/// 色卡颜色项（用于像素编辑面板的颜色列表绑定）。
/// </summary>
public sealed class PaletteColorItem
{
    public BeadColor BeadColor { get; }
    public string DisplayName { get; }
    public IBrush Brush { get; }

    public PaletteColorItem(BeadColor color)
    {
        BeadColor = color;
        DisplayName = color.Code == color.Name ? color.Code : $"{color.Code} {color.Name}";
        Brush = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(color.R, color.G, color.B));
    }
}

/// <summary>
/// 删除像素面板的颜色项（带数量）。
/// </summary>
public sealed class DeleteColorItem
{
    public BeadColor BeadColor { get; }
    public string DisplayName { get; }
    public IBrush Brush { get; }
    public int Count { get; }

    public DeleteColorItem(BeadColor color, int count)
    {
        BeadColor = color;
        DisplayName = color.Code == color.Name ? color.Code : $"{color.Code} {color.Name}";
        Brush = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(color.R, color.G, color.B));
        Count = count;
    }
}

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
        new(DisplayMode.Round, "圆珠"),
        new(DisplayMode.Hollow, "空珠")
    };

    public BeadBrandOption[] Brands { get; } =
    {
        new(BeadBrand.None, "自由色（不限制）"),
        new(BeadBrand.Mard, "MARD（291色）"),
        new(BeadBrand.ArtkalS, "Artkal S（159色）"),
        new(BeadBrand.Perler, "Perler（57色）"),
        new(BeadBrand.Hama, "Hama（53色）"),
        new(BeadBrand.Nabbi, "Nabbi（30色）")
    };

    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private string? _originalInfo;
    [ObservableProperty] private string? _outputInfo;
    [ObservableProperty] private string? _beadCount;
    [ObservableProperty] private Bitmap? _originalBitmap;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPixels))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private byte[]? _pixelatedData;
    [ObservableProperty] private int _pixelatedWidth;
    [ObservableProperty] private int _pixelatedHeight;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThresholdControlsEnabled))]
    private bool _useManualThreshold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(IsPixelInteractive))]
    [NotifyPropertyChangedFor(nameof(IsNormalView))]
    [NotifyPropertyChangedFor(nameof(CanEditPixels))]
    [NotifyPropertyChangedFor(nameof(CanDeletePixels))]
    [NotifyCanExecuteChangedFor(nameof(DeletePixelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isPixelEditing;
    [ObservableProperty] private IReadOnlyList<PaletteColorItem> _editColors = Array.Empty<PaletteColorItem>();
    [ObservableProperty] private PaletteColorItem? _selectedEditColor;
    [ObservableProperty] private bool _showCodes;
    [ObservableProperty] private bool _isEyedropping;
    [ObservableProperty] private IReadOnlyDictionary<uint, string>? _colorCodeMap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(IsPixelInteractive))]
    [NotifyPropertyChangedFor(nameof(IsNormalView))]
    [NotifyPropertyChangedFor(nameof(CanEditPixels))]
    [NotifyPropertyChangedFor(nameof(CanDeletePixels))]
    [NotifyCanExecuteChangedFor(nameof(DeletePixelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteColorCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPixelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isPixelDeleting;
    [ObservableProperty] private IReadOnlyList<DeleteColorItem> _deleteColors = Array.Empty<DeleteColorItem>();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteColorCommand))]
    private DeleteColorItem? _selectedDeleteColor;

    [ObservableProperty] private bool _useDither;

    /// <summary>选择框（归一化坐标 0~1），与控件双向绑定。</summary>
    [ObservableProperty] private Rect _selection = new(0, 0, 1, 1);

    /// <summary>是否处于编辑选择框模式。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isEditing;

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
        set
        {
            if (SetProperty(ref _horizontalSplits, Math.Max(1, value)))
                _ = AutoGenerateAsync();
        }
    }

    private double _colorMergeThreshold = 30;
    public double ColorMergeThreshold
    {
        get => _colorMergeThreshold;
        set
        {
            if (SetProperty(ref _colorMergeThreshold, Math.Max(0, Math.Min(100, value))))
                _ = AutoGenerateAsync();
        }
    }

    private ProcessModeOption _selectedMode;
    public ProcessModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
                _ = AutoGenerateAsync();
        }
    }

    private BeadBrandOption _selectedBrand;
    public BeadBrandOption SelectedBrand
    {
        get => _selectedBrand;
        set
        {
            if (SetProperty(ref _selectedBrand, value))
            {
                OnPropertyChanged(nameof(IsThresholdEnabled));
                OnPropertyChanged(nameof(IsThresholdControlsEnabled));
                OnPropertyChanged(nameof(CanEditPixels));
                OnPropertyChanged(nameof(CanDeletePixels));
                OnPropertyChanged(nameof(CanShowCodes));
                EditPixelsCommand.NotifyCanExecuteChanged();
                DeletePixelsCommand.NotifyCanExecuteChanged();
                _ = AutoGenerateAsync();
            }
        }
    }

    /// <summary>阈值控件是否可用（仅自由色模式）。</summary>
    public bool IsThresholdEnabled => _selectedBrand?.Value == BeadBrand.None;

    /// <summary>阈值具体控件（Slider/NumericUpDown/重置）是否可用：需同时满足自由色模式且勾选手动阈值。</summary>
    public bool IsThresholdControlsEnabled => IsThresholdEnabled && UseManualThreshold;

    /// <summary>是否处于正常视图（非编辑、非删除模式）。</summary>
    public bool IsNormalView => !IsPixelEditing && !IsPixelDeleting;

    /// <summary>像素网格是否可交互（编辑或删除模式）。</summary>
    public bool IsPixelInteractive => IsPixelEditing || IsPixelDeleting;

    /// <summary>是否可进入像素编辑：需已有像素化结果、选中了品牌色卡且未处于删除模式。</summary>
    public bool CanEditPixels => PixelatedData is not null && _selectedBrand?.Value != BeadBrand.None && !IsPixelDeleting;

    /// <summary>是否可进入像素删除：需已有像素化结果、选中了品牌色卡且未处于编辑模式。</summary>
    public bool CanDeletePixels => PixelatedData is not null && _selectedBrand?.Value != BeadBrand.None && !IsPixelEditing;

    /// <summary>是否可删除选中颜色：处于删除模式且选中了颜色。</summary>
    public bool CanDeleteColor => IsPixelDeleting && SelectedDeleteColor is not null;

    /// <summary>是否可撤销：处于编辑或删除模式且 undo 栈非空。</summary>
    public bool CanUndo => (IsPixelEditing || IsPixelDeleting) && _undoStack.Count > 0;

    /// <summary>是否可显示颜色编码：需选中了品牌色卡。</summary>
    public bool CanShowCodes => _selectedBrand?.Value != BeadBrand.None;

    /// <summary>是否可导出：需已有像素化结果且未处于编辑/删除模式。</summary>
    public bool CanExport => PixelatedData is not null && !IsEditing && !IsPixelEditing && !IsPixelDeleting;

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
    // 像素编辑前的快照，供取消时恢复。
    private byte[]? _pixelEditBackup;
    // 像素删除前的快照，供取消时恢复。
    private byte[]? _pixelDeleteBackup;
    // 撤销栈：每项为一次操作的若干像素变更 (像素索引, 旧R, 旧G, 旧B, 旧A)。
    private readonly Stack<List<(int index, byte r, byte g, byte b, byte a)>> _undoStack = new();
    // 取色用 RGB→PaletteColorItem 查找表（编辑模式）。
    private Dictionary<uint, PaletteColorItem> _colorToItemMap = new();
    // 取色用 RGB→DeleteColorItem 查找表（删除模式）。
    private Dictionary<uint, DeleteColorItem> _deleteColorToItemMap = new();
    // 自动生成控制：_suppressAutoGenerate 防止 LoadImage 批量赋值时重复触发，
    // _isGenerating 防止 GenerateAsync 内部改 ColorMergeThreshold 导致递归。
    private bool _suppressAutoGenerate;
    private bool _isGenerating;

    public MainWindowViewModel()
    {
        _selectedMode = Modes[1];
        _selectedDisplayMode = DisplayModes[0];
        _selectedBrand = Brands[0];
    }

    partial void OnPixelatedDataChanged(byte[]? value)
    {
        EditPixelsCommand.NotifyCanExecuteChanged();
        DeletePixelsCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnUseDitherChanged(bool value) => _ = AutoGenerateAsync();
    partial void OnUseManualThresholdChanged(bool value) => _ = AutoGenerateAsync();

    /// <summary>参数变化时自动重新生成像素化结果。</summary>
    private async Task AutoGenerateAsync()
    {
        if (_suppressAutoGenerate || _isGenerating || _sourceRgba is null || IsPixelEditing || IsPixelDeleting) return;
        await GenerateAsync();
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

    /// <summary>进入像素编辑模式，备份当前结果并加载色卡颜色列表。</summary>
    [RelayCommand(CanExecute = nameof(CanEditPixels))]
    private void EditPixels()
    {
        _pixelEditBackup = PixelatedData;
        _undoStack.Clear();

        var brand = _selectedBrand?.Value ?? BeadBrand.None;
        var colors = BeadPalettes.Get(brand);
        var items = new List<PaletteColorItem>(colors.Count);
        _colorToItemMap = new Dictionary<uint, PaletteColorItem>(colors.Count);
        foreach (var c in colors)
        {
            var item = new PaletteColorItem(c);
            items.Add(item);
            uint key = ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            _colorToItemMap[key] = item;
        }

        EditColors = items;
        SelectedEditColor = items.Count > 0 ? items[0] : null;
        IsPixelEditing = true;
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>取消像素编辑，恢复编辑前的像素化结果。</summary>
    [RelayCommand]
    private void CancelEditPixels()
    {
        if (_pixelEditBackup is not null)
            PixelatedData = _pixelEditBackup;
        _pixelEditBackup = null;
        _undoStack.Clear();
        _colorToItemMap.Clear();
        IsEyedropping = false;
        EditColors = Array.Empty<PaletteColorItem>();
        SelectedEditColor = null;
        IsPixelEditing = false;
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>应用像素编辑，保留修改后的结果。</summary>
    [RelayCommand]
    private void ApplyEditPixels()
    {
        _pixelEditBackup = null;
        _undoStack.Clear();
        _colorToItemMap.Clear();
        IsEyedropping = false;
        EditColors = Array.Empty<PaletteColorItem>();
        SelectedEditColor = null;
        IsPixelEditing = false;
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>进入像素删除模式，备份当前结果并加载图像中已使用的颜色列表。</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePixels))]
    private void DeletePixels()
    {
        _pixelDeleteBackup = PixelatedData;
        _undoStack.Clear();
        IsEyedropping = false;
        IsPixelDeleting = true;
        RefreshDeleteColors();
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>取消像素删除，恢复删除前的像素化结果。</summary>
    [RelayCommand]
    private void CancelDeletePixels()
    {
        if (_pixelDeleteBackup is not null)
            PixelatedData = _pixelDeleteBackup;
        _pixelDeleteBackup = null;
        _undoStack.Clear();
        _deleteColorToItemMap.Clear();
        IsEyedropping = false;
        DeleteColors = Array.Empty<DeleteColorItem>();
        SelectedDeleteColor = null;
        IsPixelDeleting = false;
        RefreshBeadCount();
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>应用像素删除，保留修改后的结果。</summary>
    [RelayCommand]
    private void ApplyDeletePixels()
    {
        _pixelDeleteBackup = null;
        _undoStack.Clear();
        _deleteColorToItemMap.Clear();
        IsEyedropping = false;
        DeleteColors = Array.Empty<DeleteColorItem>();
        SelectedDeleteColor = null;
        IsPixelDeleting = false;
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>删除所有与选中颜色相同的像素（设为透明）。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteColor))]
    private async Task DeleteColorAsync()
    {
        if (PixelatedData is null || SelectedDeleteColor is null) return;

        string colorCode = SelectedDeleteColor.BeadColor.Code;
        bool confirmed = await ShowConfirmDialog($"确定要删除颜色为 {colorCode} 的所有像素吗？");
        if (!confirmed) return;

        var color = SelectedDeleteColor.BeadColor;
        byte targetR = color.R, targetG = color.G, targetB = color.B;
        var data = PixelatedData;
        var changes = new List<(int, byte, byte, byte, byte)>();
        byte[] newData = (byte[])data.Clone();

        int total = PixelatedWidth * PixelatedHeight;
        for (int i = 0; i < total; i++)
        {
            int idx = i * 4;
            // 仅删除未删除的像素（alpha>0）且颜色匹配
            if (data[idx + 3] == 0) continue;
            if (data[idx] == targetR && data[idx + 1] == targetG && data[idx + 2] == targetB)
            {
                changes.Add((idx, data[idx], data[idx + 1], data[idx + 2], data[idx + 3]));
                newData[idx + 3] = 0; // 设为透明
            }
        }

        if (changes.Count == 0) return;

        _undoStack.Push(changes);
        PixelatedData = newData;
        RefreshDeleteColors();
        RefreshBeadCount();

        if (_undoStack.Count == 1)
            UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>显示确认对话框，返回用户是否点击了"确定"。</summary>
    private static async Task<bool> ShowConfirmDialog(string message)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;
        var owner = desktop.MainWindow;
        if (owner is null) return false;

        bool result = false;
        Window? dialog = null;

        var confirmBtn = new Button
        {
            Content = "确定",
            Padding = new Thickness(24, 6)
        };
        confirmBtn.Resources["ButtonBackground"] = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xF4, 0x43, 0x36));
        confirmBtn.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xFF, 0x5C, 0x5C));
        confirmBtn.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));
        confirmBtn.Resources["ButtonForeground"] = Brushes.White;
        confirmBtn.Resources["ButtonForegroundPointerOver"] = Brushes.White;
        confirmBtn.Click += (_, _) => { result = true; dialog?.Close(); };

        var cancelBtn = new Button
        {
            Content = "取消",
            Padding = new Thickness(24, 6)
        };
        cancelBtn.Click += (_, _) => { result = false; dialog?.Close(); };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(messageText, 0);
        grid.Children.Add(messageText);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        Grid.SetRow(buttonPanel, 1);
        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(confirmBtn);
        grid.Children.Add(buttonPanel);

        dialog = new Window
        {
            Title = "确认删除",
            Width = 380,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = grid
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>重新计算当前图像中使用的颜色及数量。</summary>
    private void RefreshDeleteColors()
    {
        if (PixelatedData is null || _selectedBrand is null)
        {
            DeleteColors = Array.Empty<DeleteColorItem>();
            _deleteColorToItemMap.Clear();
            SelectedDeleteColor = null;
            return;
        }

        var brand = _selectedBrand.Value;
        var palette = BeadPalettes.Get(brand);
        var paletteMap = new Dictionary<uint, BeadColor>(palette.Count);
        foreach (var c in palette)
            paletteMap[((uint)c.R << 16) | ((uint)c.G << 8) | c.B] = c;

        // 统计每种颜色的像素数量（跳过已删除的像素）
        var counts = new Dictionary<uint, int>();
        var data = PixelatedData;
        int total = PixelatedWidth * PixelatedHeight;
        for (int i = 0; i < total; i++)
        {
            int idx = i * 4;
            if (data[idx + 3] == 0) continue;
            uint key = ((uint)data[idx] << 16) | ((uint)data[idx + 1] << 8) | data[idx + 2];
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        // 构建颜色列表
        var items = new List<DeleteColorItem>(counts.Count);
        var newMap = new Dictionary<uint, DeleteColorItem>(counts.Count);
        foreach (var (key, count) in counts)
        {
            if (paletteMap.TryGetValue(key, out var beadColor))
            {
                var item = new DeleteColorItem(beadColor, count);
                items.Add(item);
                newMap[key] = item;
            }
        }

        // 按数量降序排列
        items.Sort((a, b) => b.Count.CompareTo(a.Count));

        // 保留当前选择
        DeleteColorItem? newSelection = null;
        if (SelectedDeleteColor is not null)
        {
            uint selKey = ((uint)SelectedDeleteColor.BeadColor.R << 16) |
                          ((uint)SelectedDeleteColor.BeadColor.G << 8) |
                          SelectedDeleteColor.BeadColor.B;
            newMap.TryGetValue(selKey, out newSelection);
        }
        if (newSelection is null && items.Count > 0)
            newSelection = items[0];

        _deleteColorToItemMap = newMap;
        DeleteColors = items;
        SelectedDeleteColor = newSelection;
    }

    /// <summary>撤销最近一次像素修改。</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count == 0 || PixelatedData is null) return;

        var changes = _undoStack.Pop();
        byte[] newData = (byte[])PixelatedData.Clone();
        foreach (var (index, r, g, b, a) in changes)
        {
            newData[index] = r;
            newData[index + 1] = g;
            newData[index + 2] = b;
            newData[index + 3] = a;
        }
        PixelatedData = newData;

        // 删除模式下需要刷新颜色列表和拼豆总数
        if (IsPixelDeleting)
        {
            RefreshDeleteColors();
            RefreshBeadCount();
        }

        if (_undoStack.Count == 0)
            UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>根据当前 PixelatedData 中未删除的像素数量刷新拼豆总数。</summary>
    private void RefreshBeadCount()
    {
        if (PixelatedData is null) return;
        int count = 0;
        int total = PixelatedWidth * PixelatedHeight;
        for (int i = 0; i < total; i++)
        {
            if (PixelatedData[i * 4 + 3] != 0) count++;
        }
        BeadCount = $"拼豆总数: {count}";
    }

    /// <summary>处理像素点击：取色模式下拾取颜色，编辑模式下修改像素。</summary>
    public void SetPixel(int x, int y)
    {
        if (PixelatedData is null) return;
        if (x < 0 || x >= PixelatedWidth || y < 0 || y >= PixelatedHeight) return;
        if (!IsPixelEditing && !IsPixelDeleting) return;

        // 取色模式（编辑和删除共用）
        if (IsEyedropping)
        {
            var data = PixelatedData;
            int index = (y * PixelatedWidth + x) * 4;
            // 跳过已删除的像素（alpha=0）
            if (data[index + 3] == 0) return;
            uint key = ((uint)data[index] << 16) | ((uint)data[index + 1] << 8) | data[index + 2];

            if (IsPixelEditing)
            {
                if (_colorToItemMap.TryGetValue(key, out var item))
                {
                    SelectedEditColor = item;
                    IsEyedropping = false;
                }
            }
            else // IsPixelDeleting
            {
                if (_deleteColorToItemMap.TryGetValue(key, out var item))
                {
                    SelectedDeleteColor = item;
                    IsEyedropping = false;
                }
            }
            return;
        }

        // 编辑模式：绘制像素
        if (IsPixelEditing)
        {
            if (SelectedEditColor is null) return;

            var data2 = PixelatedData;
            int idx = (y * PixelatedWidth + x) * 4;
            _undoStack.Push(new List<(int, byte, byte, byte, byte)>
            {
                (idx, data2[idx], data2[idx + 1], data2[idx + 2], data2[idx + 3])
            });

            var color = SelectedEditColor.BeadColor;
            byte[] newData = (byte[])data2.Clone();
            newData[idx] = color.R;
            newData[idx + 1] = color.G;
            newData[idx + 2] = color.B;
            PixelatedData = newData;

            if (_undoStack.Count == 1)
                UndoCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_sourceRgba is null || _isGenerating) return;
        _isGenerating = true;
        try
        {
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
                    _suppressAutoGenerate = true;
                    ColorMergeThreshold = _autoThreshold;
                    _suppressAutoGenerate = false;
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
                Mode = mode,
                Brand = _selectedBrand?.Value ?? BeadBrand.None,
                Dither = UseDither
            };

            int ps = Math.Max(1, (int)Math.Ceiling((double)cropW / hs));
            int outW = (cropW + ps - 1) / ps;
            int outH = (cropH + ps - 1) / ps;

            byte[] outRgba = await Task.Run(() => ImagePixelator.Pixelate(cropData, cropW, cropH, options));

            PixelatedData = outRgba;
            PixelatedWidth = outW;
            PixelatedHeight = outH;

            var brand = _selectedBrand?.Value ?? BeadBrand.None;
            if (brand != BeadBrand.None)
            {
                var palette = BeadPalettes.Get(brand);
                var map = new Dictionary<uint, string>(palette.Count);
                foreach (var c in palette)
                    map[((uint)c.R << 16) | ((uint)c.G << 8) | c.B] = c.Code;
                ColorCodeMap = map;
            }
            else
            {
                ColorCodeMap = null;
            }

            OutputInfo = $"像素化后分辨率: {outW}×{outH}";
            BeadCount = $"拼豆总数: {outW * outH}";
        }
        catch (Exception)
        {
        }
        finally
        {
            _isGenerating = false;
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
            _suppressAutoGenerate = true;

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

            // 将半透明像素与白色背景合成，确保所有源像素完全不透明（alpha=255）。
            // 这样 RGB 值代表实际可见颜色，渲染与导出结果一致。
            for (int i = 0; i < _sourceRgba.Length; i += 4)
            {
                byte a = _sourceRgba[i + 3];
                if (a == 255) continue;
                double af = a / 255.0;
                _sourceRgba[i] = (byte)(_sourceRgba[i] * af + 255 * (1 - af));
                _sourceRgba[i + 1] = (byte)(_sourceRgba[i + 1] * af + 255 * (1 - af));
                _sourceRgba[i + 2] = (byte)(_sourceRgba[i + 2] * af + 255 * (1 - af));
                _sourceRgba[i + 3] = 255;
            }

            // 新图片默认框选全部
            Selection = new Rect(0, 0, 1, 1);
            _appliedSelection = new Rect(0, 0, 1, 1);
            IsEditing = false;

            // 计算自动阈值并设为阈值的默认值（基于框选部分）。
            var (cropData, cropW, cropH) = ExtractCropData();
            byte[] cd = cropData;
            _autoThreshold = await Task.Run(() => ImagePixelator.ComputeAutoThreshold(cd, cropW, cropH));
            ColorMergeThreshold = _autoThreshold;
            _needsThresholdUpdate = false;

            ImagePath = path;
            OriginalInfo = $"原图分辨率: {img.Width}×{img.Height}";
            GenerateCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
            EditCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();

            // 加载后立即生成一次，提供即时反馈。
            _suppressAutoGenerate = false;
            await GenerateAsync();
        }
        catch (Exception)
        {
            _suppressAutoGenerate = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync(string format)
    {
        if (PixelatedData is null) return;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        var window = desktop.MainWindow;
        if (window is null) return;

        var ext = format.ToUpperInvariant() switch
        {
            "PNG" => "png",
            "JPG" => "jpg",
            "SVG" => "svg",
            "PDF" => "pdf",
            _ => "png"
        };

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"导出为 {format.ToUpperInvariant()}",
            DefaultExtension = ext,
            FileTypeChoices = new[]
            {
                new FilePickerFileType($"{format.ToUpperInvariant()} 文件") { Patterns = new[] { $".{ext}" } }
            },
            SuggestedFileName = $"pixelated_{PixelatedWidth}x{PixelatedHeight}"
        });

        if (file is null) return;

        var path = file.Path.LocalPath;
        try
        {
            await PixelExporter.ExportAsync(
                PixelatedData, PixelatedWidth, PixelatedHeight,
                SelectedDisplayMode.Value, ShowCodes, ColorCodeMap,
                path, format);
        }
        catch (Exception)
        {
        }
    }
}
