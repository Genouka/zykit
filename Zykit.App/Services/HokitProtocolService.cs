using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Zykit.App.Services;

/// <summary>
/// hokit:// 与 zykit:// 协议注册与解析服务。
/// 协议格式: hokit://sign?url=&lt;base64&gt;&amp;headers[]=Key:+Value&amp;source=https://...
/// zykit:// 协议与 hokit:// 完全一致，仅 scheme 不同。
/// </summary>
public class HokitProtocolService
{
    private const string HokitProtocolScheme = "hokit";
    private const string ZykitProtocolScheme = "zykit";
    private const string HokitProtocolKey = $@"Software\Classes\{HokitProtocolScheme}";
    private const string ZykitProtocolKey = $@"Software\Classes\{ZykitProtocolScheme}";

    /// <summary>待处理的协议数据（由启动参数解析后填充，供联动操作页面消费）</summary>
    public HokitProtocolData? PendingData { get; set; }

    /// <summary>当前应用可执行文件路径</summary>
    private static string AppPath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    /// <summary>检查 hokit:// 协议是否已注册</summary>
    public bool IsProtocolRegistered() => IsSchemeRegistered(HokitProtocolKey);

    /// <summary>向系统注册 hokit:// 协议</summary>
    public bool RegisterProtocol() => RegisterScheme(HokitProtocolKey, "URL:Hokit Protocol");

    /// <summary>取消注册 hokit:// 协议</summary>
    public bool UnregisterProtocol() => UnregisterScheme(HokitProtocolKey);

    /// <summary>检查 zykit:// 协议是否已注册</summary>
    public bool IsZykitProtocolRegistered() => IsSchemeRegistered(ZykitProtocolKey);

    /// <summary>向系统注册 zykit:// 协议</summary>
    public bool RegisterZykitProtocol() => RegisterScheme(ZykitProtocolKey, "URL:Zykit Protocol");

    /// <summary>取消注册 zykit:// 协议</summary>
    public bool UnregisterZykitProtocol() => UnregisterScheme(ZykitProtocolKey);

    private static bool IsSchemeRegistered(string protocolKey)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(protocolKey);
            return key?.OpenSubKey("shell\\open\\command")?.GetValue("") != null;
        }
        catch { return false; }
    }

    private static bool RegisterScheme(string protocolKey, string protocolName)
    {
        try
        {
            using var baseKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(protocolKey);
            baseKey.SetValue("", protocolName);
            baseKey.SetValue("URL Protocol", "");

            using var cmdKey = Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey($@"{protocolKey}\shell\open\command");
            cmdKey.SetValue("", $"\"{AppPath}\" \"%1\"");

            return true;
        }
        catch { return false; }
    }

    private static bool UnregisterScheme(string protocolKey)
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(protocolKey, false);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 解析 hokit:// 或 zykit:// 协议 URL，提取下载地址、请求头和来源标识。
    /// 两种协议格式完全一致，仅 scheme 不同。解析逻辑与 scheme 无关。
    /// 兼容浏览器/Windows 对 URL 进行百分号编码的情况。
    /// </summary>
    public HokitProtocolData? ParseUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            LogProtocol("ParseUrl: rawUrl 为空");
            return null;
        }

        try
        {
            // 去除可能的前后引号（Windows 协议唤起有时会带引号）
            var url = rawUrl.Trim().Trim('"');

            // 浏览器或 Windows 可能对整个 URL 进行了百分号编码
            // （例如 ? → %3F, & → %26, = → %3D），先解码恢复结构字符
            url = Uri.UnescapeDataString(url);

            LogProtocol($"ParseUrl: 解码后 URL = {url}");

            // hokit://sign?url=...&headers[]=...&source=...
            var queryIndex = url.IndexOf('?');
            if (queryIndex < 0)
            {
                LogProtocol("ParseUrl: URL 中未找到 '?'，无法解析查询串");
                return null;
            }

            var query = url.Substring(queryIndex + 1);
            var pairs = query.Split('&');
            string urlParam = "";
            var headers = new List<string>();
            string source = "";

            foreach (var pair in pairs)
            {
                var eqIndex = pair.IndexOf('=');
                if (eqIndex < 0) continue;

                var key = pair.Substring(0, eqIndex);
                var value = pair.Substring(eqIndex + 1);

                if (key == "url")
                {
                    // base64 解码（value 中的 + 和 / 应保持原样，不进行 URL 解码）
                    urlParam = DecodeBase64(value);
                    LogProtocol($"ParseUrl: url 参数原始值={value}, 解码后={urlParam}");
                }
                else if (key == "headers[]")
                {
                    // 表单格式: Key: Value
                    // URL 解码：%xx → 字符，+ → 空格
                    headers.Add(Uri.UnescapeDataString(value).Replace('+', ' '));
                }
                else if (key == "source")
                {
                    source = Uri.UnescapeDataString(value).Replace('+', ' ');
                }
            }

            if (string.IsNullOrEmpty(urlParam))
            {
                LogProtocol("ParseUrl: url 参数为空或解码失败");
                return null;
            }

            return new HokitProtocolData
            {
                Url = urlParam,
                Headers = headers,
                Source = source
            };
        }
        catch (Exception ex)
        {
            LogProtocol($"ParseUrl 异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>协议诊断日志（写入临时目录）</summary>
    private static void LogProtocol(string message)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "Zykit", "protocol.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    /// <summary>Base64 解码（兼容 URL 安全变体和 + 被解码为空格的情况）</summary>
    private static string DecodeBase64(string base64)
    {
        var sb = new StringBuilder(base64);
        // URL 传输中 + 可能被解码为空格，这里还原
        sb.Replace(' ', '+');
        // URL 安全 base64 可能将 +/ 替换为 -_
        sb.Replace('-', '+').Replace('_', '/');
        // 补齐 padding
        var pad = sb.Length % 4;
        if (pad > 0) sb.Append('=', 4 - pad);

        var bytes = Convert.FromBase64String(sb.ToString());
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 下载协议指定的文件到临时目录。
    /// </summary>
    public async Task<string> DownloadAsync(HokitProtocolData data, IProgress<(long received, long? total)>? progress = null)
    {
        using var http = new HttpClient();
        // 设置请求头
        foreach (var header in data.Headers)
        {
            var colonIndex = header.IndexOf(':');
            if (colonIndex > 0)
            {
                var name = header.Substring(0, colonIndex).Trim();
                var value = header.Substring(colonIndex + 1).Trim();
                if (!string.IsNullOrEmpty(name))
                    http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await http.GetAsync(data.Url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var fileName = ExtractFileName(data.Url, response);
        var tempPath = Path.Combine(Path.GetTempPath(), "Zykit", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(tempPath);

        var buffer = new byte[81920];
        long received = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read);
            received += read;
            progress?.Report((received, totalBytes));
        }

        return tempPath;
    }

    /// <summary>从 URL 或 Content-Disposition 提取文件名</summary>
    private static string ExtractFileName(string url, System.Net.Http.HttpResponseMessage response)
    {
        // 尝试从 Content-Disposition 提取
        if (response.Content.Headers.ContentDisposition?.FileNameStar != null)
            return response.Content.Headers.ContentDisposition.FileNameStar.Trim('"');
        if (response.Content.Headers.ContentDisposition?.FileName != null)
            return response.Content.Headers.ContentDisposition.FileName.Trim('"');

        // 从 URL 提取
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }

        return "downloaded.app";
    }
}

/// <summary>hokit:// 协议解析结果</summary>
public class HokitProtocolData
{
    /// <summary>解码后的下载地址</summary>
    public string Url { get; set; } = "";

    /// <summary>请求头数组（表单格式: Key: Value）</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>请求来源标识（仅供参考）</summary>
    public string Source { get; set; } = "";
}
