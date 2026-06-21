using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.Helpers;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class AppIdView : UserControl
{
    public AppIdView()
    {
        InitializeComponent();
        var vm = App.Services.GetRequiredService<AppIdViewModel>();
        vm.CopyToClipboard = async text =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
                await topLevel.Clipboard.SetTextAsync(text);
        };
        DataContext = vm;
    }

    private void CopySelected_Click(object? sender, RoutedEventArgs e)
        => DataGridCopyHelper.CopySelectedRows(AppIdGrid);

    private void CopyCell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string propName && !string.IsNullOrEmpty(propName))
            DataGridCopyHelper.CopyCellValue(AppIdGrid, propName);
    }
}
