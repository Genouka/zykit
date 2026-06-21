using CommunityToolkit.Mvvm.ComponentModel;
using SukiUI.Toasts;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ISukiToastManager ToastManager { get; }
    public ThemeService Theme { get; }

    public MainWindowViewModel(ISukiToastManager toastManager, ThemeService theme)
    {
        ToastManager = toastManager;
        Theme = theme;
    }
}
