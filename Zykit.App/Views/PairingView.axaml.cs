using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.Helpers;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class PairingView : UserControl
{
    public PairingView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<PairingViewModel>();
    }

    private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PairingViewModel vm && vm.SelectedPair != null)
            vm.StartEditPairCommand.Execute(null);
    }

    private void MenuEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PairingViewModel vm && vm.SelectedPair != null)
            vm.StartEditPairCommand.Execute(null);
    }

    private void MenuDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PairingViewModel vm)
            vm.DeletePairCommand.Execute(null);
    }

    private void CopySelected_Click(object? sender, RoutedEventArgs e)
        => DataGridCopyHelper.CopySelectedRows(PairGrid);

    private void CopyCell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string propName && !string.IsNullOrEmpty(propName))
            DataGridCopyHelper.CopyCellValue(PairGrid, propName);
    }
}
