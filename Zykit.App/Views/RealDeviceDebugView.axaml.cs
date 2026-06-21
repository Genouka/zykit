using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class RealDeviceDebugView : UserControl
{
    public RealDeviceDebugView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<RealDeviceDebugViewModel>();
    }

    private async void BrowseHapFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 HAP 文件以读取包名和 ACL",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("所有可安装的格式") { Patterns = new[] { "*.hap", "*.app" } },
                new FilePickerFileType("HAP 文件") { Patterns = new[] { "*.hap" } },
                new FilePickerFileType("APP 文件") { Patterns = new[] { "*.app" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } },
            }
        });

        if (files.Count > 0 && DataContext is RealDeviceDebugViewModel vm)
        {
            vm.LoadFromHap(files[0].Path.LocalPath);
        }
    }
}
