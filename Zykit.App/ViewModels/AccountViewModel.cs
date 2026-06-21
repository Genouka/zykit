using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class AccountViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly EcoService _ecoService;

    [ObservableProperty]
    private string _loginStatus = "正在检查登录状态...";

    [ObservableProperty]
    private string _userDetail = "";

    [ObservableProperty]
    private bool _isLoggedIn = false;

    [ObservableProperty]
    private bool _isLoggingIn = false;

    [ObservableProperty]
    private string _expiryInfo = "";

    public AccountViewModel(AuthService authService, EcoService ecoService)
    {
        _authService = authService;
        _ecoService = ecoService;
        _authService.LoginSuccess += OnLoginSuccess;
        _authService.LogoutSuccess += OnLogoutSuccess;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var success = await _authService.TryAutoLoginAsync();
        if (!success)
        {
            LoginStatus = "未登录";
            IsLoggedIn = false;
        }
    }

    private void OnLoginSuccess(AuthInfo auth)
    {
        LoginStatus = $"已登录: {auth.NickName}";
        UserDetail = $"用户ID: {auth.UserId}";
        IsLoggedIn = true;
        ExpiryInfo = $"登录有效期至: {auth.ExpiresAt:yyyy-MM-dd HH:mm}";
    }

    private void OnLogoutSuccess()
    {
        LoginStatus = "未登录";
        UserDetail = "";
        IsLoggedIn = false;
        ExpiryInfo = "";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            IsLoggingIn = true;
            await _authService.LoginAsync();
            BringMainWindowToFront();
        }
        catch (Exception ex)
        {
            LoginStatus = $"登录失败: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    /// <summary>OAuth 登录成功后将主窗口切换到前台</summary>
    private static void BringMainWindowToFront()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window == null) return;

        // 恢复最小化/隐藏状态
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        // Topmost 瞬时切换，强制 Windows 将窗口提到前台
        window.Topmost = true;
        window.Topmost = false;
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.Logout();
    }
}
