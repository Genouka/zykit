using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Enums;
using SukiUI.Toasts;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISdkPathProvider _sdk;
    private readonly UpdateService _update;
    private readonly ISukiToastManager _toast;
    private readonly AppSettingsService _appSettings;
    private readonly HokitProtocolService _hokitProtocol;

    /// <summary>主题服务 (供 View 绑定主题设置)</summary>
    public ThemeService Theme { get; }

    /// <summary>应用设置服务 (供 View 绑定 Hokit 兼容模式等开关)</summary>
    public AppSettingsService AppSettings { get; }

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

    /// <summary>hokit:// 协议是否已注册</summary>
    [ObservableProperty]
    private bool _isHokitProtocolRegistered = false;

    /// <summary>hokit:// 协议注册状态文本</summary>
    [ObservableProperty]
    private string _hokitProtocolStatusText = "";

    /// <summary>zykit:// 协议是否已注册</summary>
    [ObservableProperty]
    private bool _isZykitProtocolRegistered = false;

    /// <summary>zykit:// 协议注册状态文本</summary>
    [ObservableProperty]
    private string _zykitProtocolStatusText = "";

    public SettingsViewModel(ISdkPathProvider sdk, UpdateService update,
        ISukiToastManager toast, ThemeService theme, AppSettingsService appSettings,
        HokitProtocolService hokitProtocol)
    {
        _sdk = sdk;
        _update = update;
        _toast = toast;
        Theme = theme;
        _appSettings = appSettings;
        AppSettings = appSettings;
        _hokitProtocol = hokitProtocol;
        LoadSettings();
        LoadVersionInfo();
        RefreshProtocolStatus();
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

        // 发现新版本：弹出带按钮的 Toast 提示
        var url = string.IsNullOrWhiteSpace(info.DownloadUrl)
            ? UpdateService.HomePageUrl
            : info.DownloadUrl;

        var content = $"新版本 v{info.LatestVersion} 已发布，当前版本 v{_update.CurrentVersion}";
        if (!string.IsNullOrWhiteSpace(info.ReleaseDate))
            content += $"\n发布日期: {info.ReleaseDate}";
        if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            content += $"\n更新说明: {info.ReleaseNotes}";

        _toast.CreateToast()
            .WithTitle("发现新版本")
            .WithContent(content)
            .OfType(NotificationType.Information)
            .WithActionButton("立即更新", _ => OpenUrl(url), true)
            .WithActionButton("稍后", _ => { }, true, SukiButtonStyles.Accent)
            .Queue();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    /// <summary>刷新协议注册状态（hokit:// 与 zykit://）</summary>
    private void RefreshProtocolStatus()
    {
        IsHokitProtocolRegistered = _hokitProtocol.IsProtocolRegistered();
        HokitProtocolStatusText = IsHokitProtocolRegistered
            ? "hokit:// 协议已注册，浏览器点击链接可唤起本程序"
            : "hokit:// 协议未注册，点击「注册协议」按钮启用";

        IsZykitProtocolRegistered = _hokitProtocol.IsZykitProtocolRegistered();
        ZykitProtocolStatusText = IsZykitProtocolRegistered
            ? "zykit:// 协议已注册，浏览器点击链接可唤起本程序"
            : "zykit:// 协议未注册，点击「注册协议」按钮启用";
    }

    /// <summary>注册 hokit:// 协议到系统</summary>
    [RelayCommand]
    private void RegisterHokitProtocol()
    {
        var ok = _hokitProtocol.RegisterProtocol();
        RefreshProtocolStatus();

        _toast.CreateToast()
            .WithTitle(ok ? "注册成功" : "注册失败")
            .WithContent(ok ? "hokit:// 协议已注册，现在可以在浏览器中点击 hokit:// 链接唤起本程序" : "请检查权限后重试")
            .OfType(ok ? NotificationType.Success : NotificationType.Error)
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Dismiss().ByClicking()
            .Queue();
    }

    /// <summary>取消注册 hokit:// 协议</summary>
    [RelayCommand]
    private void UnregisterHokitProtocol()
    {
        var ok = _hokitProtocol.UnregisterProtocol();
        RefreshProtocolStatus();

        _toast.CreateToast()
            .WithTitle(ok ? "已取消注册" : "取消注册失败")
            .WithContent(ok ? "hokit:// 协议已取消注册" : "请检查权限后重试")
            .OfType(ok ? NotificationType.Information : NotificationType.Error)
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Dismiss().ByClicking()
            .Queue();
    }

    /// <summary>注册 zykit:// 协议到系统</summary>
    [RelayCommand]
    private void RegisterZykitProtocol()
    {
        var ok = _hokitProtocol.RegisterZykitProtocol();
        RefreshProtocolStatus();

        _toast.CreateToast()
            .WithTitle(ok ? "注册成功" : "注册失败")
            .WithContent(ok ? "zykit:// 协议已注册，现在可以在浏览器中点击 zykit:// 链接唤起本程序" : "请检查权限后重试")
            .OfType(ok ? NotificationType.Success : NotificationType.Error)
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Dismiss().ByClicking()
            .Queue();
    }

    /// <summary>取消注册 zykit:// 协议</summary>
    [RelayCommand]
    private void UnregisterZykitProtocol()
    {
        var ok = _hokitProtocol.UnregisterZykitProtocol();
        RefreshProtocolStatus();

        _toast.CreateToast()
            .WithTitle(ok ? "已取消注册" : "取消注册失败")
            .WithContent(ok ? "zykit:// 协议已取消注册" : "请检查权限后重试")
            .OfType(ok ? NotificationType.Information : NotificationType.Error)
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Dismiss().ByClicking()
            .Queue();
    }
}
