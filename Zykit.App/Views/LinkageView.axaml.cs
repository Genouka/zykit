using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class LinkageView : UserControl
{
    public LinkageView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<LinkageViewModel>();
    }

    private void ToggleLog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LinkageViewModel vm)
        {
            vm.ShowAdvancedLog = !vm.ShowAdvancedLog;
        }
    }
}
