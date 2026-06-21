using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Zykit.App.Json;
using Zykit.App.Models;

namespace Zykit.App.Services;

/// <summary>
/// 应用级设置服务：管理 Hokit 兼容模式等开关的持久化。
/// 参考 ThemeService 的持久化模式。
/// </summary>
public class AppSettingsService : ObservableObject
{
    private readonly string _settingsFile;
    private readonly ISdkPathProvider _sdk;
    private AppSettings _settings;

    /// <summary>Hokit 用户信息文件（真机路径，对应沙盒中 user\current\AppData\Local）</summary>
    private string HokitUserInfoPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ho-kit", "auth", "user_info.json");

    public AppSettingsService(ISdkPathProvider sdk)
    {
        _sdk = sdk;
        _settingsFile = Path.Combine(sdk.ConfigDir, "appsettings.json");
        _settings = Load();
    }

    /// <summary>是否启用 Hokit 兼容模式</summary>
    public bool HokitCompatibilityMode
    {
        get => _settings.HokitCompatibilityMode;
        set
        {
            if (_settings.HokitCompatibilityMode == value) return;
            _settings.HokitCompatibilityMode = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>是否在启动时自动检查新版本</summary>
    public bool AutoCheckUpdate
    {
        get => _settings.AutoCheckUpdate;
        set
        {
            if (_settings.AutoCheckUpdate == value) return;
            _settings.AutoCheckUpdate = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>
    /// 首次启动时检测 Hokit 环境：若发现 ho-kit 的 user_info.json 则自动启用兼容模式。
    /// 仅执行一次（通过 HokitDetectionCompleted 标记）。
    /// </summary>
    /// <returns>是否触发了自动启用（调用方可据此弹出 Toast）</returns>
    public bool DetectHokitOnFirstLaunch()
    {
        if (_settings.HokitDetectionCompleted) return false;
        _settings.HokitDetectionCompleted = true;

        try
        {
            if (File.Exists(HokitUserInfoPath))
            {
                _settings.HokitCompatibilityMode = true;
                Save();
                return true;
            }
        }
        catch { }
        Save();
        return false;
    }

    /// <summary>
    /// 首次启动时自动注册 zykit:// 协议。
    /// 仅执行一次（通过 ZykitProtocolAutoRegistered 标记），用户后续手动取消注册不会被再次覆盖。
    /// </summary>
    /// <param name="protocol">协议注册服务</param>
    /// <returns>是否执行了注册（调用方可据此弹出 Toast）</returns>
    public bool AutoRegisterZykitProtocolOnFirstLaunch(HokitProtocolService protocol)
    {
        if (_settings.ZykitProtocolAutoRegistered) return false;
        _settings.ZykitProtocolAutoRegistered = true;

        try
        {
            if (!protocol.IsZykitProtocolRegistered())
            {
                var ok = protocol.RegisterZykitProtocol();
                Save();
                return ok;
            }
        }
        catch { }
        Save();
        return false;
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsFile)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize(json, ZykitJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, ZykitJsonContext.Default.AppSettings);
            File.WriteAllText(_settingsFile, json);
        }
        catch { }
    }
}
