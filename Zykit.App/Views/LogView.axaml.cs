using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.Helpers;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<LogViewModel>();
    }

    private void CopySelected_Click(object? sender, RoutedEventArgs e)
        => DataGridCopyHelper.CopySelectedRows(LogGrid);
}
