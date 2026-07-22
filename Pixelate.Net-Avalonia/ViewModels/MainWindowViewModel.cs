using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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
/// 分割数量选项（预设值或自定义）。
/// </summary>
public sealed record SplitSizeOption(int Value, string DisplayName, bool IsCustom);

/// <summary>
/// 分割方向选项（带中文显示名）。
/// </summary>
public sealed record SplitDirectionOption(SplitDirection Value, string DisplayName);

/// <summary>
/// 底板尺寸选项（带中文显示名）。
/// </summary>
public sealed record BoardSizeOption(int Value, string DisplayName);

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
/// IsEyedropper=true 时表示"取色"项（透明色），选中后进入取色状态。
/// </summary>
public sealed class DeleteColorItem
{
    public BeadColor BeadColor { get; }
    public string DisplayName { get; }
    public IBrush Brush { get; }
    public int Count { get; }
    public bool IsEyedropper { get; }
    /// <summary>是否显示数量（取色项不显示）。</summary>
    public bool ShowCount => !IsEyedropper;

    public DeleteColorItem(BeadColor color, int count)
    {
        BeadColor = color;
        DisplayName = color.Code == color.Name ? color.Code : $"{color.Code} {color.Name}";
        Brush = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(color.R, color.G, color.B));
        Count = count;
        IsEyedropper = false;
    }

    private DeleteColorItem(string displayName, IBrush brush)
    {
        BeadColor = default;
        DisplayName = displayName;
        Brush = brush;
        Count = 0;
        IsEyedropper = true;
    }

    /// <summary>创建"取色"项，色块用棋盘格表示透明。</summary>
    public static DeleteColorItem CreateEyedropper() => new("取色", CreateCheckerboardBrush());

    private static IBrush CreateCheckerboardBrush()
    {
        var drawing = new DrawingGroup();
        using (var ctx = drawing.Open())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, 9, 9));
            ctx.DrawRectangle(new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)), null, new Rect(9, 0, 9, 9));
            ctx.DrawRectangle(new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)), null, new Rect(0, 9, 9, 9));
            ctx.DrawRectangle(Brushes.White, null, new Rect(9, 9, 9, 9));
        }
        return new DrawingBrush
        {
            Drawing = drawing,
            DestinationRect = new RelativeRect(0, 0, 18, 18, RelativeUnit.Absolute),
            TileMode = TileMode.None
        };
    }
}

/// <summary>
/// 颜色选择对话框（A/B）的列表项。
/// </summary>
public sealed class ColorPickerItem
{
    public BeadColor Color { get; }
    public string DisplayName { get; }
    public string? CountText { get; }

    public ColorPickerItem(BeadColor color, int? count)
    {
        Color = color;
        DisplayName = color.Code == color.Name ? color.Code : $"{color.Code} {color.Name}";
        CountText = count?.ToString();
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

    public SplitSizeOption[] SplitSizes { get; } =
    {
        new(25, "25", false),
        new(52, "52", false),
        new(78, "78", false),
        new(104, "104", false),
        new(0, "自定义", true)
    };

    public SplitDirectionOption[] SplitDirections { get; } =
    {
        new(SplitDirection.Auto, "自动"),
        new(SplitDirection.Horizontal, "水平"),
        new(SplitDirection.Vertical, "竖直")
    };

    public BoardSizeOption[] BoardSizes { get; } =
    {
        new(25, "25×25"),
        new(52, "52×52"),
        new(78, "78×78"),
        new(104, "104×104")
    };

    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private string? _originalInfo;
    [ObservableProperty] private string? _outputInfo;
    [ObservableProperty] private string? _beadCount;
    [ObservableProperty] private string? _boardInfo;
    [ObservableProperty] private Bitmap? _originalBitmap;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPixels))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanPrint))]
    [NotifyPropertyChangedFor(nameof(HasPixelatedData))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
    private byte[]? _pixelatedData;
    [ObservableProperty] private int _pixelatedWidth;
    [ObservableProperty] private int _pixelatedHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanPrint))]
    [NotifyPropertyChangedFor(nameof(IsPixelInteractive))]
    [NotifyPropertyChangedFor(nameof(IsNormalView))]
    [NotifyPropertyChangedFor(nameof(CanEditPixels))]
    [NotifyPropertyChangedFor(nameof(CanDeletePixels))]
    [NotifyCanExecuteChangedFor(nameof(DeletePixelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSourceColorDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowTargetColorDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceColorsCommand))]
    private bool _isPixelEditing;

    /// <summary>编辑模式：逐点修改（默认按下）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowSourceColorDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowTargetColorDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceColorsCommand))]
    private bool _isPerPixelEdit = true;

    /// <summary>编辑模式：批量修改。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowSourceColorDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowTargetColorDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceColorsCommand))]
    private bool _isBatchEdit;

    /// <summary>批量编辑：被替换颜色。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceColorsCommand))]
    private PaletteColorItem? _batchEditSourceColor;

    /// <summary>批量编辑：目标颜色。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceColorsCommand))]
    private PaletteColorItem? _batchEditTargetColor;

    [ObservableProperty] private IReadOnlyList<PaletteColorItem> _editColors = Array.Empty<PaletteColorItem>();
    [ObservableProperty] private PaletteColorItem? _selectedEditColor;
    [ObservableProperty] private bool _showCodes = true;
    [ObservableProperty] private bool _isEyedropping;
    [ObservableProperty] private IReadOnlyDictionary<uint, string>? _colorCodeMap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanPrint))]
    [NotifyPropertyChangedFor(nameof(IsPixelInteractive))]
    [NotifyPropertyChangedFor(nameof(IsNormalView))]
    [NotifyPropertyChangedFor(nameof(CanEditPixels))]
    [NotifyPropertyChangedFor(nameof(CanDeletePixels))]
    [NotifyCanExecuteChangedFor(nameof(DeletePixelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteColorCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPixelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
    private bool _isPixelDeleting;

    /// <summary>删除模式：逐点删除（默认按下）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteColorCommand))]
    private bool _isPerPixelDelete = true;

    /// <summary>删除模式：批量删除。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteColorCommand))]
    private bool _isBatchDelete;

    [ObservableProperty] private IReadOnlyList<DeleteColorItem> _deleteColors = Array.Empty<DeleteColorItem>();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteColorCommand))]
    private DeleteColorItem? _selectedDeleteColor;

    /// <summary>选择框（归一化坐标 0~1），与控件双向绑定。</summary>
    [ObservableProperty] private Rect _selection = new(0, 0, 1, 1);

    /// <summary>是否处于编辑选择框模式。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanPrint))]
    [NotifyPropertyChangedFor(nameof(CanShowCodes))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
    private bool _isEditing;

    private DisplayModeOption _selectedDisplayMode;
    public DisplayModeOption SelectedDisplayMode
    {
        get => _selectedDisplayMode;
        set => SetProperty(ref _selectedDisplayMode, value);
    }

    // 底板尺寸选择。
    private BoardSizeOption _selectedBoardSize;
    public BoardSizeOption SelectedBoardSize
    {
        get => _selectedBoardSize;
        set
        {
            if (SetProperty(ref _selectedBoardSize, value))
            {
                RefreshSplitSizeAvailability();
                _ = AutoGenerateAsync();
            }
        }
    }

    /// <summary>是否启用多板拼接（勾选时分割数量可超过底板尺寸）。</summary>
    [ObservableProperty] private bool _useMultiBoard;

    partial void OnUseMultiBoardChanged(bool value)
    {
        RefreshSplitSizeAvailability();
        _ = AutoGenerateAsync();
    }

    /// <summary>编辑模式状态按钮互斥：按下"逐点修改"时弹起"批量修改"。</summary>
    partial void OnIsPerPixelEditChanged(bool value)
    {
        if (value && _isBatchEdit)
            IsBatchEdit = false;
        // 防止两个按钮都弹起：若用户点击已按下的按钮，恢复为按下
        if (!value && !_isBatchEdit)
            IsPerPixelEdit = true;
    }

    /// <summary>编辑模式状态按钮互斥：按下"批量修改"时弹起"逐点修改"。</summary>
    partial void OnIsBatchEditChanged(bool value)
    {
        if (value && _isPerPixelEdit)
            IsPerPixelEdit = false;
        if (!value && !_isPerPixelEdit)
            IsBatchEdit = true;
    }

    /// <summary>删除模式状态按钮互斥：按下"逐点删除"时弹起"批量删除"。</summary>
    partial void OnIsPerPixelDeleteChanged(bool value)
    {
        if (value && _isBatchDelete)
            IsBatchDelete = false;
        if (!value && !_isBatchDelete)
            IsPerPixelDelete = true;
        // 切换到逐点删除时退出取色状态
        if (value && _isEyedropping)
            IsEyedropping = false;
    }

    /// <summary>删除模式状态按钮互斥：按下"批量删除"时弹起"逐点删除"。</summary>
    partial void OnIsBatchDeleteChanged(bool value)
    {
        if (value && _isPerPixelDelete)
            IsPerPixelDelete = false;
        if (!value && !_isPerPixelDelete)
            IsBatchDelete = true;
        // 切换到逐点删除时退出取色状态
        if (!value && _isEyedropping)
            IsEyedropping = false;
    }

    /// <summary>选中删除颜色变化时：选中"取色"项则进入取色状态。</summary>
    partial void OnSelectedDeleteColorChanged(DeleteColorItem? value)
    {
        IsEyedropping = IsPixelDeleting && IsBatchDelete && value?.IsEyedropper == true;
        DeleteColorCommand.NotifyCanExecuteChanged();
    }

    // 分割数量选择（预设值或自定义）。
    private SplitSizeOption _selectedSplitSize;
    public SplitSizeOption SelectedSplitSize
    {
        get => _selectedSplitSize;
        set
        {
            if (SetProperty(ref _selectedSplitSize, value))
            {
                OnPropertyChanged(nameof(IsCustomSplitSize));
                _ = AutoGenerateAsync();
            }
        }
    }

    /// <summary>是否为自定义分割数量（显示输入框）。</summary>
    public bool IsCustomSplitSize => _selectedSplitSize?.IsCustom == true;

    /// <summary>当前可选的分割数量选项：未勾选多板拼接时仅保留 ≤ 底板尺寸的选项。</summary>
    public IReadOnlyList<SplitSizeOption> AvailableSplitSizes
    {
        get
        {
            int board = _selectedBoardSize?.Value ?? 52;
            var list = new List<SplitSizeOption>(SplitSizes.Length);
            foreach (var opt in SplitSizes)
            {
                if (opt.IsCustom || opt.Value <= board || UseMultiBoard)
                    list.Add(opt);
            }
            return list;
        }
    }

    /// <summary>自定义分割数量输入的最大值：未勾选多板拼接时为底板尺寸，否则为 1024。</summary>
    public int CustomSplitsMax => UseMultiBoard ? 1024 : (_selectedBoardSize?.Value ?? 52);

    // 自定义分割数量输入（仅 IsCustomSplitSize 时可见）。
    private double _customSplits = 52;
    public double CustomSplits
    {
        get => _customSplits;
        set
        {
            int max = CustomSplitsMax;
            double clamped = Math.Max(1, Math.Min(max, value));
            if (SetProperty(ref _customSplits, clamped))
                _ = AutoGenerateAsync();
        }
    }

    /// <summary>当前生效的分割数量（预设值或自定义值取整）。</summary>
    public int EffectiveSplits =>
        IsCustomSplitSize ? Math.Max(1, (int)Math.Round(_customSplits)) : (_selectedSplitSize?.Value ?? 52);

    /// <summary>当底板尺寸或多板拼接变化时，刷新可选分割数量并约束当前选择。</summary>
    private void RefreshSplitSizeAvailability()
    {
        // 先修正当前选中的分割数，确保在更新可选列表前 SelectedSplitSize 已有效，
        // 避免 ComboBox 因旧选中项不在新列表中而清空为 null。
        if (!UseMultiBoard)
        {
            int board = _selectedBoardSize?.Value ?? 52;
            // null（初始化或被 ComboBox 清空）或预设值超过底板尺寸时，回退到底板尺寸对应的选项。
            if (_selectedSplitSize is null || (!_selectedSplitSize.IsCustom && _selectedSplitSize.Value > board))
            {
                var fallback = SplitSizes.FirstOrDefault(o => !o.IsCustom && o.Value == board)
                               ?? SplitSizes.First(o => !o.IsCustom);
                _selectedSplitSize = fallback;
                OnPropertyChanged(nameof(SelectedSplitSize));
                OnPropertyChanged(nameof(IsCustomSplitSize));
            }
        }

        // 约束自定义值不超过上限（自定义模式下上限随底板尺寸变化）。
        int max = UseMultiBoard ? 1024 : (_selectedBoardSize?.Value ?? 52);
        if (_customSplits > max)
        {
            _customSplits = max;
            OnPropertyChanged(nameof(CustomSplits));
        }

        // 最后更新可选列表和上限，此时 SelectedSplitSize 已是有效值，ComboBox 不会清空。
        OnPropertyChanged(nameof(AvailableSplitSizes));
        OnPropertyChanged(nameof(CustomSplitsMax));
    }

    // 分割方向选择。
    private SplitDirectionOption _selectedSplitDirection;
    public SplitDirectionOption SelectedSplitDirection
    {
        get => _selectedSplitDirection;
        set
        {
            if (SetProperty(ref _selectedSplitDirection, value))
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
                OnPropertyChanged(nameof(CanEditPixels));
                OnPropertyChanged(nameof(CanDeletePixels));
                OnPropertyChanged(nameof(CanShowCodes));
                EditPixelsCommand.NotifyCanExecuteChanged();
                DeletePixelsCommand.NotifyCanExecuteChanged();
                _ = AutoGenerateAsync();
            }
        }
    }

    /// <summary>是否处于正常视图（非编辑、非删除模式）。</summary>
    public bool IsNormalView => !IsPixelEditing && !IsPixelDeleting;

    /// <summary>像素网格是否可交互（编辑或删除模式）。</summary>
    public bool IsPixelInteractive => IsPixelEditing || IsPixelDeleting;

    /// <summary>是否可进入像素编辑：需已有像素化结果、选中了品牌色卡且未处于删除模式。</summary>
    public bool CanEditPixels => PixelatedData is not null && _selectedBrand?.Value != BeadBrand.None && !IsPixelDeleting;

    /// <summary>是否可进入像素删除：需已有像素化结果、选中了品牌色卡且未处于编辑模式。</summary>
    public bool CanDeletePixels => PixelatedData is not null && _selectedBrand?.Value != BeadBrand.None && !IsPixelEditing;

    /// <summary>是否可删除选中颜色：处于批量删除模式且选中了非取色颜色。</summary>
    public bool CanDeleteColor => IsPixelDeleting && IsBatchDelete && SelectedDeleteColor is not null && !SelectedDeleteColor.IsEyedropper;

    /// <summary>是否可替换颜色：处于批量编辑模式且已选择被替换颜色和目标颜色。</summary>
    public bool CanReplaceColors => IsPixelEditing && IsBatchEdit && BatchEditSourceColor is not null && BatchEditTargetColor is not null;

    /// <summary>是否可打开被替换颜色对话框：处于批量编辑模式。</summary>
    public bool CanShowSourceColorDialog => IsPixelEditing && IsBatchEdit;

    /// <summary>是否可打开目标颜色对话框：处于批量编辑模式。</summary>
    public bool CanShowTargetColorDialog => IsPixelEditing && IsBatchEdit;

    /// <summary>是否可撤销：处于编辑或删除模式且 undo 栈非空。</summary>
    public bool CanUndo => (IsPixelEditing || IsPixelDeleting) && _undoStack.Count > 0;

    /// <summary>是否可显示颜色编码：需选中了品牌色卡，且未处于原图编辑状态。</summary>
    public bool CanShowCodes => _selectedBrand?.Value != BeadBrand.None && !IsEditing;

    /// <summary>是否可导出：需已有像素化结果且未处于编辑/删除模式。</summary>
    public bool CanExport => PixelatedData is not null && !IsEditing && !IsPixelEditing && !IsPixelDeleting;

    /// <summary>是否可打印：与导出条件一致。</summary>
    public bool CanPrint => CanExport;

    /// <summary>是否可查看拼豆统计详情：需已有像素化结果且未处于编辑/删除模式。</summary>
    public bool CanShowDetails => PixelatedData is not null && !IsEditing && !IsPixelEditing && !IsPixelDeleting;

    /// <summary>是否已有像素化结果（用于控制"详情"按钮的可见性）。</summary>
    public bool HasPixelatedData => PixelatedData is not null;

    // 已加载的源像素数据，供反复生成使用。
    private byte[]? _sourceRgba;
    private int _sourceWidth;
    private int _sourceHeight;
    // 底板布局信息：单张底板边长、水平/竖直方向底板数。
    private int _boardSize = 52;
    private int _boardsX = 1;
    private int _boardsY = 1;
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
    // _isGenerating 防止 GenerateAsync 内部重入；
    // _pendingRegenerate 标记生成期间有参数变更，需在当前生成结束后补一次重算。
    private bool _suppressAutoGenerate;
    private bool _isGenerating;
    private bool _pendingRegenerate;

    public MainWindowViewModel()
    {
        _selectedMode = Modes[1];
        _selectedDisplayMode = DisplayModes[0];
        _selectedBrand = Brands[1]; // 默认 MARD（291色）
        _selectedBoardSize = BoardSizes[1]; // 默认 52×52
        _selectedSplitSize = SplitSizes[1]; // 默认 52
        _selectedSplitDirection = SplitDirections[0]; // 默认 自动
    }

    partial void OnPixelatedDataChanged(byte[]? value)
    {
        EditPixelsCommand.NotifyCanExecuteChanged();
        DeletePixelsCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        ShowDetailsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPixelatedData));
    }

    /// <summary>参数变化时自动重新生成像素化结果。</summary>
    private async Task AutoGenerateAsync()
    {
        if (_suppressAutoGenerate || _sourceRgba is null || IsPixelEditing || IsPixelDeleting) return;
        // 生成期间发生的参数变更标记为待重算，避免丢失用户最新的设置。
        if (_isGenerating)
        {
            _pendingRegenerate = true;
            return;
        }
        await GenerateAsync();
    }

    private bool CanGenerate => _sourceRgba is not null;

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

        // 初始化批量编辑状态
        IsPerPixelEdit = true;
        IsBatchEdit = false;
        BatchEditSourceColor = GetMostFrequentPixelColor();
        BatchEditTargetColor = items.Count > 0 ? items[0] : null;

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
        BatchEditSourceColor = null;
        BatchEditTargetColor = null;
        IsPerPixelEdit = true;
        IsBatchEdit = false;
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
        BatchEditSourceColor = null;
        BatchEditTargetColor = null;
        IsPerPixelEdit = true;
        IsBatchEdit = false;
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
        IsPerPixelDelete = true;
        IsBatchDelete = false;
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
        IsPerPixelDelete = true;
        IsBatchDelete = false;
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
        IsPerPixelDelete = true;
        IsBatchDelete = false;
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

    /// <summary>
    /// 重新计算当前图像中使用的颜色及数量。
    /// 列表第一行为"取色"项（透明色），后续按数量降序排列。
    /// 默认选中第二行（数量最多的颜色）。
    /// </summary>
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
        var items = new List<DeleteColorItem>(counts.Count + 1);
        var newMap = new Dictionary<uint, DeleteColorItem>(counts.Count);

        // 第一行：取色项（透明色）
        var eyedropper = DeleteColorItem.CreateEyedropper();

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

        // 在最前面插入取色项
        items.Insert(0, eyedropper);

        // 保留当前选择（取色项或已选颜色）
        DeleteColorItem? newSelection = null;
        if (SelectedDeleteColor is not null)
        {
            if (SelectedDeleteColor.IsEyedropper)
            {
                // 保持取色状态
                newSelection = eyedropper;
            }
            else
            {
                uint selKey = ((uint)SelectedDeleteColor.BeadColor.R << 16) |
                              ((uint)SelectedDeleteColor.BeadColor.G << 8) |
                              SelectedDeleteColor.BeadColor.B;
                newMap.TryGetValue(selKey, out newSelection);
            }
        }
        // 默认选中第二行（数量最多的颜色）
        if (newSelection is null && items.Count > 1)
            newSelection = items[1];

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

        // 批量编辑模式下刷新被替换颜色（可能恢复已替换的颜色）
        if (IsPixelEditing && IsBatchEdit)
        {
            BatchEditSourceColor = GetMostFrequentPixelColor();
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

    /// <summary>获取当前像素画中出现次数最多的色卡颜色。</summary>
    private PaletteColorItem? GetMostFrequentPixelColor()
    {
        if (PixelatedData is null || _colorToItemMap.Count == 0) return null;

        var counts = new Dictionary<uint, int>();
        var data = PixelatedData;
        int total = PixelatedWidth * PixelatedHeight;
        for (int i = 0; i < total; i++)
        {
            int idx = i * 4;
            if (data[idx + 3] == 0) continue;
            uint key = ((uint)data[idx] << 16) | ((uint)data[idx + 1] << 8) | data[idx + 2];
            if (_colorToItemMap.ContainsKey(key))
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        uint bestKey = 0;
        int bestCount = -1;
        foreach (var (key, count) in counts)
        {
            if (count > bestCount)
            {
                bestCount = count;
                bestKey = key;
            }
        }

        return _colorToItemMap.TryGetValue(bestKey, out var item) ? item : null;
    }

    /// <summary>获取当前像素画中所有色卡颜色及数量，按数量降序排列。</summary>
    private List<(BeadColor color, int? count)> GetPixelArtColorsWithCounts()
    {
        var result = new List<(BeadColor, int?)>();
        if (PixelatedData is null || _selectedBrand is null) return result;

        var brand = _selectedBrand.Value;
        var palette = BeadPalettes.Get(brand);
        var paletteMap = new Dictionary<uint, BeadColor>(palette.Count);
        foreach (var c in palette)
            paletteMap[((uint)c.R << 16) | ((uint)c.G << 8) | c.B] = c;

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

        foreach (var (key, count) in counts)
        {
            if (paletteMap.TryGetValue(key, out var beadColor))
                result.Add((beadColor, count));
        }

        result.Sort((a, b) => b.Item2.GetValueOrDefault().CompareTo(a.Item2.GetValueOrDefault()));
        return result;
    }

    /// <summary>打开"被替换颜色"对话框（对话框 A）：显示像素画中所有颜色及数量，单列。</summary>
    [RelayCommand(CanExecute = nameof(CanShowSourceColorDialog))]
    private async Task ShowSourceColorDialogAsync()
    {
        var items = GetPixelArtColorsWithCounts();
        if (items.Count == 0) return;

        var initial = BatchEditSourceColor?.BeadColor ?? items[0].color;
        var selected = await ShowColorPickerDialogAsync("选择被替换颜色", items, initial, 1, true);
        if (selected is { } beadColor && _colorToItemMap.TryGetValue(
            ((uint)beadColor.R << 16) | ((uint)beadColor.G << 8) | beadColor.B, out var item))
        {
            BatchEditSourceColor = item;
        }
    }

    /// <summary>打开"目标颜色"对话框（对话框 B）：显示色卡中所有颜色，多列。</summary>
    [RelayCommand(CanExecute = nameof(CanShowTargetColorDialog))]
    private async Task ShowTargetColorDialogAsync()
    {
        if (EditColors.Count == 0) return;

        var items = new List<(BeadColor, int?)>(EditColors.Count);
        foreach (var editItem in EditColors)
            items.Add((editItem.BeadColor, null));

        var initial = BatchEditTargetColor?.BeadColor ?? items[0].Item1;
        var selected = await ShowColorPickerDialogAsync("选择目标颜色", items, initial, 3, false);
        if (selected is { } beadColor && _colorToItemMap.TryGetValue(
            ((uint)beadColor.R << 16) | ((uint)beadColor.G << 8) | beadColor.B, out var item))
        {
            BatchEditTargetColor = item;
        }
    }

    /// <summary>批量替换：将所有被替换颜色的像素修改为目标颜色。</summary>
    [RelayCommand(CanExecute = nameof(CanReplaceColors))]
    private void ReplaceColors()
    {
        if (PixelatedData is null || BatchEditSourceColor is null || BatchEditTargetColor is null) return;

        var sourceColor = BatchEditSourceColor.BeadColor;
        var targetColor = BatchEditTargetColor.BeadColor;

        // 同色不替换
        if (sourceColor.R == targetColor.R && sourceColor.G == targetColor.G && sourceColor.B == targetColor.B)
            return;

        var data = PixelatedData;
        var changes = new List<(int, byte, byte, byte, byte)>();
        byte[] newData = (byte[])data.Clone();

        int total = PixelatedWidth * PixelatedHeight;
        for (int i = 0; i < total; i++)
        {
            int idx = i * 4;
            if (data[idx + 3] == 0) continue; // 跳过已删除的像素
            if (data[idx] == sourceColor.R && data[idx + 1] == sourceColor.G && data[idx + 2] == sourceColor.B)
            {
                changes.Add((idx, data[idx], data[idx + 1], data[idx + 2], data[idx + 3]));
                newData[idx] = targetColor.R;
                newData[idx + 1] = targetColor.G;
                newData[idx + 2] = targetColor.B;
            }
        }

        if (changes.Count == 0) return;

        _undoStack.Push(changes);
        PixelatedData = newData;

        // 替换后更新被替换颜色为当前数量最多的颜色
        BatchEditSourceColor = GetMostFrequentPixelColor();

        if (_undoStack.Count == 1)
            UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 颜色选择对话框（对话框 A/B 共用）。
    /// 显示颜色列表，单选，返回选中的颜色。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="items">颜色列表（颜色 + 可选数量）。</param>
    /// <param name="initialSelection">初始选中颜色。</param>
    /// <param name="columns">列数（1=单列，3=多列）。</param>
    /// <param name="showCount">是否显示数量。</param>
    private static async Task<BeadColor?> ShowColorPickerDialogAsync(
        string title,
        IReadOnlyList<(BeadColor color, int? count)> items,
        BeadColor? initialSelection,
        int columns,
        bool showCount)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        var owner = desktop.MainWindow;
        if (owner is null) return null;

        BeadColor? result = null;
        Window? dialog = null;

        // 构建列表项
        var pickerItems = new List<ColorPickerItem>(items.Count);
        int initialIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            var (color, count) = items[i];
            pickerItems.Add(new ColorPickerItem(color, count));

            if (initialSelection is { } init &&
                init.R == color.R && init.G == color.G && init.B == color.B)
            {
                initialIndex = i;
            }
        }

        // 构建 ListBox
        var listBox = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            SelectionMode = SelectionMode.Single,
            ItemsSource = pickerItems
        };

        if (columns > 1)
        {
            listBox.ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = columns });
        }

        listBox.ItemTemplate = new FuncDataTemplate<ColorPickerItem>((item, _) =>
        {
            var grid = new Grid
            {
                ColumnDefinitions = showCount
                    ? new ColumnDefinitions("Auto,*,Auto")
                    : new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(2)
            };

            var colorBlock = new Border
            {
                Width = 18,
                Height = 18,
                Background = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(item.Color.R, item.Color.G, item.Color.B)),
                BorderBrush = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(colorBlock, 0);

            var nameText = new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 1);

            grid.Children.Add(colorBlock);
            grid.Children.Add(nameText);

            if (showCount && item.CountText is not null)
            {
                var countText = new TextBlock
                {
                    Text = item.CountText,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                Grid.SetColumn(countText, 2);
                grid.Children.Add(countText);
            }

            return grid;
        });

        if (initialIndex >= 0)
            listBox.SelectedIndex = initialIndex;

        // 确定按钮
        var okBtn = new Button
        {
            Content = "确定",
            Padding = new Thickness(24, 6)
        };
        okBtn.Resources["ButtonBackground"] = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
        okBtn.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0x5D, 0xBD, 0x5D));
        okBtn.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0x3D, 0x91, 0x40));
        okBtn.Resources["ButtonForeground"] = Brushes.White;
        okBtn.Resources["ButtonForegroundPointerOver"] = Brushes.White;
        okBtn.Click += (_, _) =>
        {
            if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < pickerItems.Count)
                result = pickerItems[listBox.SelectedIndex].Color;
            dialog?.Close();
        };

        // 取消按钮
        var cancelBtn = new Button
        {
            Content = "取消",
            Padding = new Thickness(24, 6)
        };
        cancelBtn.Click += (_, _) => { result = null; dialog?.Close(); };

        // 布局
        var scrollViewer = new ScrollViewer
        {
            Content = listBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(okBtn);

        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(10)
        };
        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(scrollViewer);
        mainGrid.Children.Add(buttonPanel);

        dialog = new Window
        {
            Title = title,
            Width = columns > 1 ? 480 : 360,
            Height = 520,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = mainGrid
        };

        // 隐藏最小化和最大化按钮
        dialog.Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var handle = dialog.TryGetPlatformHandle();
            if (handle is null) return;
            int style = GetWindowLong(handle.Handle, GWL_STYLE);
            style &= ~WS_MINIMIZEBOX;
            style &= ~WS_MAXIMIZEBOX;
            SetWindowLong(handle.Handle, GWL_STYLE, style);
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// 处理像素点击：
    /// - 编辑模式 + 逐点修改：将点击的像素修改为选中的色卡颜色
    /// - 删除模式 + 逐点删除：将点击的像素设为透明（删除）
    /// - 删除模式 + 批量删除 + 取色状态：拾取点击像素的颜色并在列表中选中
    /// </summary>
    public void SetPixel(int x, int y)
    {
        if (PixelatedData is null) return;
        if (x < 0 || x >= PixelatedWidth || y < 0 || y >= PixelatedHeight) return;
        if (!IsPixelEditing && !IsPixelDeleting) return;

        var data = PixelatedData;
        int index = (y * PixelatedWidth + x) * 4;

        // 删除模式 + 批量删除 + 取色状态：拾取颜色
        if (IsPixelDeleting && IsBatchDelete && IsEyedropping)
        {
            // 跳过已删除的像素（alpha=0）
            if (data[index + 3] == 0) return;
            uint key = ((uint)data[index] << 16) | ((uint)data[index + 1] << 8) | data[index + 2];
            if (_deleteColorToItemMap.TryGetValue(key, out var item))
            {
                // 选中被取的颜色，OnSelectedDeleteColorChanged 会将 IsEyedropping 设为 false
                SelectedDeleteColor = item;
            }
            return;
        }

        // 编辑模式 + 逐点修改：替换像素颜色
        if (IsPixelEditing && IsPerPixelEdit)
        {
            if (SelectedEditColor is null) return;

            _undoStack.Push(new List<(int, byte, byte, byte, byte)>
            {
                (index, data[index], data[index + 1], data[index + 2], data[index + 3])
            });

            var color = SelectedEditColor.BeadColor;
            byte[] newData = (byte[])data.Clone();
            newData[index] = color.R;
            newData[index + 1] = color.G;
            newData[index + 2] = color.B;
            PixelatedData = newData;

            if (_undoStack.Count == 1)
                UndoCommand.NotifyCanExecuteChanged();
            return;
        }

        // 删除模式 + 逐点删除：删除单个像素
        if (IsPixelDeleting && IsPerPixelDelete)
        {
            // 跳过已删除的像素
            if (data[index + 3] == 0) return;

            _undoStack.Push(new List<(int, byte, byte, byte, byte)>
            {
                (index, data[index], data[index + 1], data[index + 2], data[index + 3])
            });

            byte[] newData = (byte[])data.Clone();
            newData[index + 3] = 0; // 设为透明
            PixelatedData = newData;

            if (_undoStack.Count == 1)
                UndoCommand.NotifyCanExecuteChanged();
            return;
        }

        // 其他情况（批量编辑、批量删除非取色）：点击不做处理
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_sourceRgba is null) return;
        // 生成期间被直接调用（如 Apply/LoadImage）时，标记待重算避免丢失。
        if (_isGenerating)
        {
            _pendingRegenerate = true;
            return;
        }
        _isGenerating = true;
        try
        {
            var (cropData, cropW, cropH) = ExtractCropData();
            if (cropW <= 0 || cropH <= 0) return;

            // 若需要重算自动阈值（裁剪变化时），更新 _autoThreshold；
            // 阈值由算法自动计算，无手动覆盖。
            if (_needsThresholdUpdate)
            {
                byte[] cd = cropData;
                _autoThreshold = await Task.Run(() => ImagePixelator.ComputeAutoThreshold(cd, cropW, cropH));
                _needsThresholdUpdate = false;
            }

            int splits = EffectiveSplits;
            if (splits < 1) splits = 1;
            int threshold = _autoThreshold;
            if (threshold < 0) threshold = 0;
            if (threshold > 100) threshold = 100;
            var mode = _selectedMode?.Value ?? ProcessMode.Realistic;
            var userDir = _selectedSplitDirection?.Value ?? SplitDirection.Auto;
            bool multiBoard = UseMultiBoard;
            int boardSize = _selectedBoardSize?.Value ?? 52;

            // 解析 Auto 方向：
            // 未勾选多板拼接 → 主方向为尺寸更大的方向（让长边填满底板，短边自动容纳）；
            // 勾选多板拼接 → 主方向为尺寸更小的方向（让短边填满一张底板，长边跨多张）。
            SplitDirection resolvedDir;
            if (userDir != SplitDirection.Auto)
            {
                resolvedDir = userDir;
            }
            else if (!multiBoard)
            {
                resolvedDir = cropW >= cropH ? SplitDirection.Horizontal : SplitDirection.Vertical;
            }
            else
            {
                resolvedDir = cropW <= cropH ? SplitDirection.Horizontal : SplitDirection.Vertical;
            }

            // 未勾选多板拼接时，通过 MaxOutputDimension 约束输出宽高均不超过底板尺寸，
            // 无论分割方向如何选择都能保证像素画完全装入单张底板。
            var options = new PixelateOptions
            {
                Splits = splits,
                SplitDirection = resolvedDir,
                ColorMergeThreshold = threshold,
                Mode = mode,
                Brand = _selectedBrand?.Value ?? BeadBrand.None,
                MaxOutputDimension = multiBoard ? 0 : boardSize
            };

            // 算法层输出像素画原始尺寸。
            byte[] artRgba = await Task.Run(() => ImagePixelator.Pixelate(cropData, cropW, cropH, options));
            var (outW, outH) = ImagePixelator.GetOutputSize(cropW, cropH, splits, resolvedDir, options.MaxOutputDimension);

            // 计算底板布局：
            // 未勾选多板拼接时，强制单张底板（1×1），像素画已由算法约束不超出底板尺寸；
            // 勾选多板拼接时，画布按 outW/outH 向上取整为底板尺寸的整数倍，可跨多张底板。
            _boardSize = boardSize;
            if (multiBoard)
            {
                _boardsX = Math.Max(1, (int)Math.Ceiling(outW / (double)boardSize));
                _boardsY = Math.Max(1, (int)Math.Ceiling(outH / (double)boardSize));
            }
            else
            {
                _boardsX = 1;
                _boardsY = 1;
            }
            int canvasW = _boardsX * boardSize;
            int canvasH = _boardsY * boardSize;

            // 将像素画居中放置于画布上，空白区域为透明像素。
            byte[] canvasData = await Task.Run(() => CompositeOnCanvas(artRgba, outW, outH, canvasW, canvasH));

            PixelatedData = canvasData;
            PixelatedWidth = canvasW;
            PixelatedHeight = canvasH;

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
            RefreshBeadCount();
            int totalBoards = _boardsX * _boardsY;
            BoardInfo = totalBoards <= 1
                ? $"底板: {_boardsX}×{_boardsY}"
                : $"底板: {_boardsX}×{_boardsY} (共{totalBoards}张)";
        }
        catch (Exception)
        {
        }
        finally
        {
            _isGenerating = false;
            // 生成期间若有参数变更，补一次重算以使用用户最新的设置。
            if (_pendingRegenerate)
            {
                _pendingRegenerate = false;
                _ = AutoGenerateAsync();
            }
        }
    }

    /// <summary>
    /// 将像素画居中合成到画布上，空白区域为透明 (alpha=0)。
    /// 像素画超出画布时按居中裁剪。
    /// </summary>
    private static byte[] CompositeOnCanvas(byte[] artRgba, int outW, int outH, int canvasW, int canvasH)
    {
        byte[] canvas = new byte[canvasW * canvasH * 4]; // 默认全 0 = 透明
        int offsetX = (canvasW - outW) / 2;
        int offsetY = (canvasH - outH) / 2;

        // 计算像素画与画布的交集（处理 art 大于 canvas 的裁剪情况）。
        int dstX0 = Math.Max(0, offsetX);
        int dstY0 = Math.Max(0, offsetY);
        int srcX0 = Math.Max(0, -offsetX);
        int srcY0 = Math.Max(0, -offsetY);
        int copyW = Math.Min(outW - srcX0, canvasW - dstX0);
        int copyH = Math.Min(outH - srcY0, canvasH - dstY0);
        if (copyW <= 0 || copyH <= 0) return canvas;

        for (int y = 0; y < copyH; y++)
        {
            int srcRow = ((srcY0 + y) * outW + srcX0) * 4;
            int dstRow = ((dstY0 + y) * canvasW + dstX0) * 4;
            Buffer.BlockCopy(artRgba, srcRow, canvas, dstRow, copyW * 4);
        }
        return canvas;
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
            // 保留原始 alpha 通道，供算法层对透明区域进行二值化（输出 0 或 255）。
            _sourceWidth = img.Width;
            _sourceHeight = img.Height;
            _sourceRgba = new byte[img.Width * img.Height * 4];
            img.CopyPixelDataTo(_sourceRgba);

            // 新图片默认框选全部
            Selection = new Rect(0, 0, 1, 1);
            _appliedSelection = new Rect(0, 0, 1, 1);
            IsEditing = false;

            // 计算自动阈值（基于框选部分）。
            var (cropData, cropW, cropH) = ExtractCropData();
            byte[] cd = cropData;
            _autoThreshold = await Task.Run(() => ImagePixelator.ComputeAutoThreshold(cd, cropW, cropH));
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
                path, format, transparentBackground: true,
                boardSize: _boardSize);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>渲染像素化结果并启动打印：Windows 弹出系统打印对话框，其他平台用系统 PDF 查看器打开。</summary>
    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        if (PixelatedData is null) return;

        // 获取主窗口引用，非 Windows 平台用于调用 Launcher 打开 PDF
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            owner = desktop.MainWindow;

        try
        {
            await PixelPrinter.PrintAsync(
                PixelatedData, PixelatedWidth, PixelatedHeight,
                SelectedDisplayMode.Value, ShowCodes, ColorCodeMap,
                $"pixelated_{PixelatedWidth}x{PixelatedHeight}",
                owner, _boardSize);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>弹出拼豆统计详情对话框：第一行显示总数和颜色数，下方为按数量降序排列的颜色列表。</summary>
    [RelayCommand(CanExecute = nameof(CanShowDetails))]
    private async Task ShowDetailsAsync()
    {
        if (PixelatedData is null) return;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        var owner = desktop.MainWindow;
        if (owner is null) return;

        // 计算颜色统计：跳过已删除（透明）像素。
        var brand = _selectedBrand?.Value ?? BeadBrand.None;
        var palette = BeadPalettes.Get(brand);
        var paletteMap = new Dictionary<uint, BeadColor>(palette.Count);
        foreach (var c in palette)
            paletteMap[((uint)c.R << 16) | ((uint)c.G << 8) | c.B] = c;

        var counts = new Dictionary<uint, int>();
        var data = PixelatedData;
        int total = PixelatedWidth * PixelatedHeight;
        int totalCount = 0;
        for (int i = 0; i < total; i++)
        {
            int idx = i * 4;
            if (data[idx + 3] == 0) continue;
            totalCount++;
            uint key = ((uint)data[idx] << 16) | ((uint)data[idx + 1] << 8) | data[idx + 2];
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        // 构建颜色列表（按数量降序），未在色卡中的颜色以 RGB 文本展示。
        var items = new List<(BeadColor color, int count)>(counts.Count);
        foreach (var (key, count) in counts)
        {
            if (paletteMap.TryGetValue(key, out var bc))
            {
                items.Add((bc, count));
            }
            else
            {
                byte r = (byte)(key >> 16);
                byte g = (byte)(key >> 8);
                byte b = (byte)key;
                items.Add((new BeadColor("RGB", $"RGB({r},{g},{b})", r, g, b), count));
            }
        }
        items.Sort((a, b) => b.count.CompareTo(a.count));

        var dialog = BuildDetailsDialog(totalCount, items.Count, items);
        await dialog.ShowDialog(owner);
    }

    /// <summary>构建拼豆统计详情对话框窗口。</summary>
    private static Window BuildDetailsDialog(int totalCount, int colorCount, List<(BeadColor color, int count)> items)
    {
        // 顶部统计信息
        var header = new TextBlock
        {
            Text = $"拼豆总数: {totalCount}      颜色总数: {colorCount}",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 颜色列表
        var listPanel = new StackPanel { Spacing = 2 };
        foreach (var (color, count) in items)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(0, 2, 12, 2)
            };

            var colorBlock = new Border
            {
                Width = 18,
                Height = 18,
                Background = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(colorBlock, 0);

            var nameText = new TextBlock
            {
                Text = color.Code == color.Name ? color.Code : $"{color.Code} {color.Name}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 1);

            var countText = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(countText, 2);

            row.Children.Add(colorBlock);
            row.Children.Add(nameText);
            row.Children.Add(countText);
            listPanel.Children.Add(row);
        }

        var scrollViewer = new ScrollViewer
        {
            Content = listPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var dialog = new Window();

        // 主布局：顶部统计(自动) + 中间滚动列表(占满剩余)
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(10)
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(scrollViewer, 1);
        mainGrid.Children.Add(header);
        mainGrid.Children.Add(scrollViewer);

        dialog.Title = "拼豆统计详情";
        dialog.Width = 360;
        dialog.Height = 520;
        dialog.CanResize = false;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = mainGrid;

        // 隐藏标题栏的最小化和最大化按钮（仅保留关闭按钮）。Windows 平台用 P/Invoke 修改 WS。
        dialog.Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var handle = dialog.TryGetPlatformHandle();
            if (handle is null) return;
            int style = GetWindowLong(handle.Handle, GWL_STYLE);
            style &= ~WS_MINIMIZEBOX;
            style &= ~WS_MAXIMIZEBOX;
            SetWindowLong(handle.Handle, GWL_STYLE, style);
        };

        return dialog;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private const int GWL_STYLE = -16;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;

    /// <summary>在系统默认浏览器中打开 GitHub 仓库地址。</summary>
    [RelayCommand]
    private async Task OpenGithubAsync()
    {
        const string url = "https://github.com/simplex86/Pixelate.Net";
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        var mainWindow = desktop.MainWindow;
        if (mainWindow is null) return;

        try
        {
            await mainWindow.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception)
        {
        }
    }
}
