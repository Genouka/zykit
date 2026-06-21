namespace Zykit.App.Models;

/// <summary>
/// 主题设置持久化模型 (存储于 ~/.zykit/theme.json)
/// 枚举值以 int 形式存储以兼容 Native AOT 源生成 JSON。
/// </summary>
public class ThemeSettings
{
    /// <summary>是否暗色模式</summary>
    public bool IsDarkMode { get; set; } = false;

    /// <summary>主题色 (SukiColor 枚举值: 0=Blue, 1=Green, 2=Red, 3=Orange)</summary>
    public int ColorTheme { get; set; } = 0;

    /// <summary>背景样式 (SukiBackgroundStyle 枚举值: 0=Gradient, 1=GradientSoft, 2=GradientDarker, 3=Flat, 4=Bubble)</summary>
    public int BackgroundStyle { get; set; } = 4;

    /// <summary>是否启用背景动画</summary>
    public bool BackgroundAnimationEnabled { get; set; } = false;

    /// <summary>是否启用背景过渡效果</summary>
    public bool BackgroundTransitionsEnabled { get; set; } = false;
}
