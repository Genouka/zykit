using System;
using System.IO;
using System.Text.Json;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using SukiUI;
using SukiUI.Enums;
using Zykit.App.Json;
using Zykit.App.Models;

namespace Zykit.App.Services;

/// <summary>
/// 主题服务：管理明暗主题、主题色、背景样式的状态与持久化，
/// 并将设置应用到 SukiTheme 与 SukiWindow。
/// 参考: https://kikipoulet.github.io/SukiUI/zh/documentation/theming/
/// </summary>
public class ThemeService : ObservableObject
{
    private readonly string _settingsFile;
    private ThemeSettings _settings;

    public ThemeService(ISdkPathProvider sdk)
    {
        _settingsFile = Path.Combine(sdk.ConfigDir, "theme.json");
        _settings = Load();
        ApplyAll();
    }

    // ── 可用选项 (供 UI 绑定) ──

    public SukiColor[] ColorThemeOptions { get; } = { SukiColor.Blue, SukiColor.Green, SukiColor.Red, SukiColor.Orange };

    public SukiBackgroundStyle[] BackgroundStyleOptions { get; } =
    {
        SukiBackgroundStyle.Gradient,
        SukiBackgroundStyle.GradientSoft,
        SukiBackgroundStyle.GradientDarker,
        SukiBackgroundStyle.Flat,
        SukiBackgroundStyle.Bubble
    };

    // ── 明暗主题 ──

    public bool IsDarkMode
    {
        get => _settings.IsDarkMode;
        set
        {
            if (_settings.IsDarkMode == value) return;
            _settings.IsDarkMode = value;
            OnPropertyChanged();
            ApplyBaseTheme();
            Save();
        }
    }

    // ── 主题色 ──

    public SukiColor ColorTheme
    {
        get => (SukiColor)_settings.ColorTheme;
        set
        {
            if ((SukiColor)_settings.ColorTheme == value) return;
            _settings.ColorTheme = (int)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ColorThemeIndex));
            ApplyColorTheme();
            Save();
        }
    }

    /// <summary>主题色索引 (供 ComboBox SelectedIndex 绑定)</summary>
    public int ColorThemeIndex
    {
        get => _settings.ColorTheme;
        set => ColorTheme = (SukiColor)value;
    }

    // ── 背景样式 ──

    public SukiBackgroundStyle BackgroundStyle
    {
        get => (SukiBackgroundStyle)_settings.BackgroundStyle;
        set
        {
            if ((SukiBackgroundStyle)_settings.BackgroundStyle == value) return;
            _settings.BackgroundStyle = (int)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundStyleIndex));
            Save();
        }
    }

    /// <summary>背景样式索引 (供 ComboBox SelectedIndex 绑定)</summary>
    public int BackgroundStyleIndex
    {
        get => _settings.BackgroundStyle;
        set => BackgroundStyle = (SukiBackgroundStyle)value;
    }

    // ── 背景动画 ──

    public bool BackgroundAnimationEnabled
    {
        get => _settings.BackgroundAnimationEnabled;
        set
        {
            if (_settings.BackgroundAnimationEnabled == value) return;
            _settings.BackgroundAnimationEnabled = value;
            OnPropertyChanged();
            Save();
        }
    }

    // ── 背景过渡 ──

    public bool BackgroundTransitionsEnabled
    {
        get => _settings.BackgroundTransitionsEnabled;
        set
        {
            if (_settings.BackgroundTransitionsEnabled == value) return;
            _settings.BackgroundTransitionsEnabled = value;
            OnPropertyChanged();
            Save();
        }
    }

    // ── 应用设置 ──

    private void ApplyAll()
    {
        try
        {
            var theme = SukiTheme.GetInstance();
            theme.ChangeBaseTheme(IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light);
            theme.ChangeColorTheme(ColorTheme);
        }
        catch { /* SukiTheme 尚未初始化时忽略 */ }
    }

    private void ApplyBaseTheme()
    {
        try { SukiTheme.GetInstance().ChangeBaseTheme(IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light); }
        catch { }
    }

    private void ApplyColorTheme()
    {
        try { SukiTheme.GetInstance().ChangeColorTheme(ColorTheme); }
        catch { }
    }

    // ── 持久化 ──

    private ThemeSettings Load()
    {
        if (!File.Exists(_settingsFile)) return new ThemeSettings();
        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize(json, ZykitJsonContext.Default.ThemeSettings) ?? new ThemeSettings();
        }
        catch { return new ThemeSettings(); }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, ZykitJsonContext.Default.ThemeSettings);
            File.WriteAllText(_settingsFile, json);
        }
        catch { }
    }
}
