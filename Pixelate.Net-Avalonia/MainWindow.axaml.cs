using Avalonia.Controls;
using Pixelate.Net.Avalonia.Controls;
using Pixelate.Net.Avalonia.ViewModels;

namespace Pixelate.Net.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void PixelGridControl_PixelClicked(object? sender, PixelClickedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetPixel(e.X, e.Y);
        }
    }
}
