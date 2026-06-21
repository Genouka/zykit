namespace Zykit.App.Models;

/// <summary>
/// 应用级设置持久化模型 (存储于 ~/.zykit/appsettings.json)。
/// </summary>
public class AppSettings
{
    /// <summary>是否启用 Hokit 兼容模式（使用内置 ho-kit.p12 / ho-kit.csr 签名）</summary>
    public bool HokitCompatibilityMode { get; set; } = false;

    /// <summary>是否已完成首次启动时的 Hokit 环境检测</summary>
    public bool HokitDetectionCompleted { get; set; } = false;

    /// <summary>是否已在首次启动时自动注册 zykit:// 协议</summary>
    public bool ZykitProtocolAutoRegistered { get; set; } = false;

    /// <summary>是否在启动时自动检查新版本</summary>
    public bool AutoCheckUpdate { get; set; } = true;
}
