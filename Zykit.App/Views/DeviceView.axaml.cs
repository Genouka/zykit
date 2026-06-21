using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.Helpers;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class DeviceView : UserControl
{
    public DeviceView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DeviceViewModel>();
    }

    private void DeviceGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Double-click on device: could show details or auto-fill registration form
    }

    private void CopySelected_Click(object? sender, RoutedEventArgs e)
        => DataGridCopyHelper.CopySelectedRows(DeviceGrid);

    private void CopyCell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string propName && !string.IsNullOrEmpty(propName))
            DataGridCopyHelper.CopyCellValue(DeviceGrid, propName);
    }
}
