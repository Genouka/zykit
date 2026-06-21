using System;

namespace Zykit.App.Models;

/// <summary>
/// 应用版本信息 (构建时由 MSBuild Target 自动生成 version.json 嵌入资源)
/// </summary>
public class AppVersionInfo
{
    /// <summary>语义化版本号，例如 "1.0.0"</summary>
    public string Version { get; set; } = "0.0.0";

    /// <summary>文件版本号 (与 Assembly FileVersion 一致)</summary>
    public string FileVersion { get; set; } = "0.0.0";

    /// <summary>构建时间 (UTC ISO 8601)</summary>
    public string BuildTime { get; set; } = "";

    /// <summary>解析后的构建时间</summary>
    public DateTime BuildTimeUtc =>
        DateTime.TryParse(BuildTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.MinValue;
}
