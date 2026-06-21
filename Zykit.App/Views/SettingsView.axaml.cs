using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SettingsViewModel>();
    }

    private async void BrowseSdkPath_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择SDK路径",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is SettingsViewModel vm)
        {
            vm.SdkPath = folders[0].Path.LocalPath;
            vm.ValidateSdkCommand.Execute(null);
        }
    }
}
