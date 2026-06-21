using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Zykit.App.Generated;
using Zykit.App.Json;
using Zykit.App.Models;

namespace Zykit.App.Services;

/// <summary>
/// 应用版本与更新检查服务。
/// 负责读取编译期版本信息，并从远程拉取 update.json 进行版本比对。
/// </summary>
public class UpdateService
{
    /// <summary>远程 update.json 地址</summary>
    public const string UpdateJsonUrl = "https://zhiyue.qiumingsanyu.top/api/zykit/update.json";

    /// <summary>官网地址</summary>
    public const string HomePageUrl = "https://zhiyue.qiumingsanyu.top";

    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly LogService _log;
    private readonly AppVersionInfo _current;

    public UpdateService(LogService log)
    {
        _log = log;
        // 版本信息来自构建时生成的编译期常量 (AOT 友好，无需运行时资源加载)
        _current = new AppVersionInfo
        {
            Version = VersionInfo.Version,
            FileVersion = VersionInfo.FileVersion,
            BuildTime = VersionInfo.BuildTime
        };
    }

    /// <summary>当前应用版本信息</summary>
    public AppVersionInfo Current => _current;

    /// <summary>当前版本号字符串</summary>
    public string CurrentVersion => _current.Version;

    /// <summary>
    /// 从远程拉取 update.json。
    /// 使用 Task.Run 将 HTTP 请求放到线程池线程，避免 DNS 解析/TLS 握手等
    /// 同步初始化操作阻塞 UI 线程。
    /// </summary>
    public async Task<UpdateInfo> FetchLatestAsync()
    {
        _log.LogRequest("GET", UpdateJsonUrl);

        // 在线程池线程执行 HTTP 请求 (DNS 解析、代理检测、TLS 握手等可能含同步部分)
        int statusCode;
        string json;
        try
        {
            (statusCode, json) = await Task.Run(async () =>
            {
                using var resp = await _http.GetAsync(UpdateJsonUrl).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ((int)resp.StatusCode, body);
            });
        }
        catch (TaskCanceledException)
        {
            throw new Exception("检查更新超时，请检查网络连接后重试");
        }

        _log.LogResponse("GET", UpdateJsonUrl, statusCode, json);

        if (statusCode < 200 || statusCode >= 300)
            throw new Exception($"检查更新失败: HTTP {statusCode}");

        var info = JsonSerializer.Deserialize(json, ZykitJsonContext.Default.UpdateInfo)
                   ?? throw new Exception("检查更新失败: 远程返回的 JSON 解析为空");
        return info;
    }

    /// <summary>
    /// 比较版本号，判断 <paramref name="remote"/> 是否比 <paramref name="local"/> 更新。
    /// 支持简单语义化版本 (x.y.z)，缺位补 0。
    /// </summary>
    public static bool IsNewer(string local, string remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        if (string.IsNullOrWhiteSpace(local)) return true;

        if (!TryParseVersion(local, out var l)) return false;
        if (!TryParseVersion(remote, out var r)) return false;

        if (r.Major != l.Major) return r.Major > l.Major;
        if (r.Minor != l.Minor) return r.Minor > l.Minor;
        return r.Build > l.Build;
    }

    private static bool TryParseVersion(string s, out (int Major, int Minor, int Build) v)
    {
        v = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().Split('.', '-', '+');
        if (parts.Length == 0) return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        int minor = 0, build = 0;
        if (parts.Length > 1) int.TryParse(parts[1], out minor);
        if (parts.Length > 2) int.TryParse(parts[2], out build);
        v = (major, minor, build);
        return true;
    }
}
