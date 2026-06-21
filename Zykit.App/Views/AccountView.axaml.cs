using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<AccountViewModel>();
    }
}
