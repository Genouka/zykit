using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.Helpers;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ProfileViewModel>();
    }

    private void ProfileGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProfileViewModel vm && vm.SelectedProfile != null)
        {
            if (vm.SelectedProfile.IsCloud && !vm.SelectedProfile.IsLocal)
                vm.DownloadProfileCommand.Execute(null);
        }
    }

    private void CopySelected_Click(object? sender, RoutedEventArgs e)
        => DataGridCopyHelper.CopySelectedRows(ProfileGrid);

    private void CopyCell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string propName && !string.IsNullOrEmpty(propName))
            DataGridCopyHelper.CopyCellValue(ProfileGrid, propName);
    }
}
