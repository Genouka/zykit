namespace Zykit.App.Models;

/// <summary>
/// 远程更新信息 (对应 https://zhiyue.qiumingsanyu.top/api/zykit/update.json)
/// </summary>
public class UpdateInfo
{
    /// <summary>最新版本号，例如 "1.1.0"</summary>
    public string LatestVersion { get; set; } = "";

    /// <summary>最低兼容版本 (低于此版本强制更新)，可空</summary>
    public string MinimumVersion { get; set; } = "";

    /// <summary>更新包下载地址</summary>
    public string DownloadUrl { get; set; } = "";

    /// <summary>官网地址 (可空，缺省时使用内置官网)</summary>
    public string HomePageUrl { get; set; } = "";

    /// <summary>本次更新说明</summary>
    public string ReleaseNotes { get; set; } = "";

    /// <summary>发布日期 (yyyy-MM-dd)</summary>
    public string ReleaseDate { get; set; } = "";
}
