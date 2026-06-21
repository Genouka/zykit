using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISdkPathProvider _sdk;
    private readonly UpdateService _update;
    private readonly ISukiToastManager _toast;

    /// <summary>主题服务 (供 View 绑定主题设置)</summary>
    public ThemeService Theme { get; }

    [ObservableProperty]
    private string _sdkPath = "";

    [ObservableProperty]
    private string _hdcPath = "";

    [ObservableProperty]
    private string _signToolPath = "";

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private string _keytoolPath = "";

    [ObservableProperty]
    private string _configDir = "";

    [ObservableProperty]
    private bool _sdkValid = false;

    [ObservableProperty]
    private string _sdkValidationMessage = "";

    /// <summary>当前应用版本号 (来自构建时生成的 version.json)</summary>
    [ObservableProperty]
    private string _appVersion = "";

    /// <summary>构建时间显示文本</summary>
    [ObservableProperty]
    private string _buildTimeText = "";

    /// <summary>是否正在检查更新</summary>
    [ObservableProperty]
    private bool _isCheckingUpdate = false;

    /// <summary>检查更新按钮文本</summary>
    [ObservableProperty]
    private string _checkUpdateButtonText = "检查更新";

    public SettingsViewModel(ISdkPathProvider sdk, UpdateService update,
        ISukiToastManager toast, ThemeService theme)
    {
        _sdk = sdk;
        _update = update;
        _toast = toast;
        Theme = theme;
        LoadSettings();
        LoadVersionInfo();
    }

    private void LoadSettings()
    {
        SdkPath = _sdk.SdkPath;
        ConfigDir = _sdk.ConfigDir;
        UpdatePaths();
    }

    private void LoadVersionInfo()
    {
        AppVersion = $"v{_update.CurrentVersion}";
        var builtAt = _update.Current.BuildTimeUtc;
        BuildTimeText = builtAt == DateTime.MinValue
            ? ""
            : $"构建时间: {builtAt.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private void UpdatePaths()
    {
        HdcPath = _sdk.HdcPath;
        SignToolPath = _sdk.SignToolJarPath;
        JavaPath = _sdk.JavaPath;
        KeytoolPath = _sdk.KeytoolPath;
    }

    [RelayCommand]
    private void ValidateSdk()
    {
        _sdk.SdkPath = SdkPath;
        UpdatePaths();

        var hdcExists = File.Exists(HdcPath);
        var signToolExists = File.Exists(SignToolPath);

        if (hdcExists && signToolExists)
        {
            SdkValid = true;
            SdkValidationMessage = "SDK路径有效，所有必需工具已找到";
        }
        else
        {
            SdkValid = false;
            var missing = new System.Collections.Generic.List<string>();
            if (!hdcExists) missing.Add("hdc.exe");
            if (!signToolExists) missing.Add("hap-sign-tool.jar");
            SdkValidationMessage = $"缺少工具: {string.Join(", ", missing)}";
        }
    }

    [RelayCommand]
    private void BrowseSdkPath()
    {
        // This will be handled by the View
    }

    /// <summary>打开官方网站</summary>
    [RelayCommand]
    private void OpenHomePage()
    {
        OpenUrl(UpdateService.HomePageUrl);
    }

    /// <summary>检查更新</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        CheckUpdateButtonText = "检查中...";

        UpdateInfo info;
        try
        {
            info = await _update.FetchLatestAsync();
        }
        catch (Exception ex)
        {
            _toast.CreateToast()
                .WithTitle("检查更新失败")
                .WithContent(ex.Message)
                .OfType(NotificationType.Error)
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Dismiss().ByClicking()
                .Queue();
            IsCheckingUpdate = false;
            CheckUpdateButtonText = "检查更新";
            return;
        }

        IsCheckingUpdate = false;
        CheckUpdateButtonText = "检查更新";

        var hasUpdate = UpdateService.IsNewer(_update.CurrentVersion, info.LatestVersion);

        if (!hasUpdate)
        {
            _toast.CreateToast()
                .WithTitle("已是最新版本")
                .WithContent($"当前版本 v{_update.CurrentVersion} 已是最新")
                .OfType(NotificationType.Success)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
            return;
        }

        // 发现新版本：直接跳转到下载页
        var url = string.IsNullOrWhiteSpace(info.DownloadUrl)
            ? UpdateService.HomePageUrl
            : info.DownloadUrl;
        OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
