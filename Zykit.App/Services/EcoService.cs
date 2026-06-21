using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class EcoService
{
    private string _oauth2Token = "";
    private string _userId = "";
    private string _nickName = "";
    private readonly string _baseUrl = "https://connect-api.cloud.huawei.com/api";
    private readonly HttpClient _http;
    private readonly LogService _logService;

    public static readonly string[] AclList =
    {
        "ohos.permission.READ_AUDIO",
        "ohos.permission.WRITE_AUDIO",
        "ohos.permission.READ_IMAGEVIDEO",
        "ohos.permission.WRITE_IMAGEVIDEO",
        "ohos.permission.SHORT_TERM_WRITE_IMAGEVIDEO",
        "ohos.permission.READ_CONTACTS",
        "ohos.permission.WRITE_CONTACTS",
        "ohos.permission.SYSTEM_FLOAT_WINDOW",
        "ohos.permission.ACCESS_DDK_USB",
        "ohos.permission.ACCESS_DDK_HID",
        "ohos.permission.INPUT_MONITORING",
        "ohos.permission.INTERCEPT_INPUT_EVENT",
        "ohos.permission.READ_PASTEBOARD"
    };

    public EcoService(LogService logService)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _logService = logService;
    }

    public void InitAuth(AuthInfo auth)
    {
        _oauth2Token = auth.AccessToken;
        _userId = auth.UserId;
        _nickName = auth.NickName;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_oauth2Token);

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, JsonNode? data = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("oauth2Token", _oauth2Token);
        req.Headers.Add("teamId", _userId);
        req.Headers.Add("uid", _userId);
        if (data != null)
        {
            req.Content = new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json");
        }
        return req;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, JsonNode? data = null)
    {
        using var req = CreateRequest(method, url, data);
        _logService.LogRequest(method.Method, url, data != null ? data.ToJsonString() : null);
        using var resp = await _http.SendAsync(req);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logService.Error($"HTTP {resp.StatusCode}: {method.Method} {url}", "Network");
            throw new Exception("登录信息过期或无效");
        }

        var json = await resp.Content.ReadAsStringAsync();
        _logService.LogResponse(method.Method, url, (int)resp.StatusCode, json);

        if (string.IsNullOrWhiteSpace(json))
            throw new Exception($"AGC接口返回空响应: {method.Method} {url}");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new Exception($"AGC接口返回非JSON响应: {json[..Math.Min(json.Length, 200)]}");
        }
        using var _doc = doc;
        var root = doc.RootElement;

        if (root.TryGetProperty("ret", out var ret))
        {
            var code = ret.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            if (code != 0)
            {
                var msg = ret.TryGetProperty("msg", out var m) ? m.GetString() : "请求失败";
                throw new Exception($"AGC错误[{code}]: {msg}");
            }
        }

        return root.Clone();
    }

    #region 认证

    public async Task<AuthInfo> GetTokenByTempTokenAsync(string tempToken)
    {
        var token = tempToken.Split('&')[0].Replace("tempToken=", "");
        var url = $"https://cn.devecostudio.huawei.com/authrouter/auth/api/temptoken/check?site=CN&tempToken={token}&appid=1007&version=0.0.0";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "Mozilla/5.0");
        using var resp = await _http.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"获取JWT Token失败: {resp.StatusCode}");

        var jwtToken = (await resp.Content.ReadAsStringAsync()).Trim();

        var url2 = "https://cn.devecostudio.huawei.com/authrouter/auth/api/jwToken/check";
        var req2 = new HttpRequestMessage(HttpMethod.Get, url2);
        req2.Headers.Add("jwtToken", jwtToken);
        req2.Headers.Add("refresh", "false");
        req2.Headers.Add("User-Agent", "Mozilla/5.0");
        using var resp2 = await _http.SendAsync(req2);

        if (!resp2.IsSuccessStatusCode)
            throw new Exception($"获取用户信息失败: {resp2.StatusCode}");

        var json = await resp2.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var userInfo = doc.RootElement.GetProperty("userInfo");

        return new AuthInfo
        {
            AccessToken = userInfo.TryGetProperty("accessToken", out var at) ? at.GetString() ?? "" : "",
            UserId = userInfo.TryGetProperty("userId", out var uid) ? uid.GetString() ?? "" : "",
            NickName = userInfo.TryGetProperty("nickName", out var nn) ? nn.GetString() ?? "" : "",
            TeamId = userInfo.TryGetProperty("userId", out var tid) ? tid.GetString() ?? "" : ""
        };
    }

    public async Task<JsonElement> GetUserTeamListAsync() =>
        await SendAsync(HttpMethod.Get, $"{_baseUrl}/ups/user-permission-service/v1/user-team-list");

    #endregion

    #region 证书管理 (AGC Provisioning API v3)

    /// <summary>申请证书 (调试/发布/In-house/二进制)</summary>
    public async Task<CloudCertInfo> CreateCertAsync(string certName, int certType, string csr)
    {
        var result = await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v3/cert",
            new JsonObject { ["csr"] = csr, ["certName"] = certName, ["certType"] = certType });

        var certInfo = result.TryGetProperty("certInfo", out var ci) ? ci : default;
        if (certInfo.ValueKind == JsonValueKind.Undefined)
            throw new Exception("创建证书失败: 未返回证书信息");

        return ParseCertInfo(certInfo);
    }

    /// <summary>查询证书列表</summary>
    public async Task<List<CloudCertInfo>> GetCertListAsync(int? certType = null, List<string>? certIds = null)
    {
        var body = new JsonObject();
        if (certType.HasValue) body["certType"] = certType.Value;
        if (certIds != null && certIds.Count > 0)
            body["certIds"] = new JsonArray(certIds.Select(c => (JsonNode)c).ToArray());

        var result = await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v3/cert/list", body);

        var certList = new List<CloudCertInfo>();
        if (result.TryGetProperty("certList", out var cl))
        {
            foreach (var item in cl.EnumerateArray())
                certList.Add(ParseCertInfo(item));
        }
        // Also check "certInfo" (single or array)
        if (result.TryGetProperty("certInfo", out var ci) && ci.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ci.EnumerateArray())
                certList.Add(ParseCertInfo(item));
        }
        return certList;
    }

    /// <summary>删除证书</summary>
    public async Task DeleteCertsAsync(List<string> certIds) =>
        await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v2/cert/delete",
            new JsonObject { ["certIds"] = new JsonArray(certIds.Select(c => (JsonNode)c).ToArray()) });

    private static CloudCertInfo ParseCertInfo(JsonElement el)
    {
        return new CloudCertInfo
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            CertName = el.TryGetProperty("certName", out var cn) ? cn.GetString() ?? "" : "",
            CertType = el.TryGetProperty("certType", out var ct) ? ct.GetInt32() : 0,
            CreateTime = el.TryGetProperty("createTime", out var crt) ? crt.GetInt64() : 0,
            ExpireTime = el.TryGetProperty("expireTime", out var et) ? et.GetInt64() : 0,
            CertDownloadUrl = el.TryGetProperty("certDownloadUrl", out var cdu) ? cdu.GetString() ?? "" : "",
        };
    }

    #endregion

    #region 设备管理 (AGC Provisioning API v2)

    /// <summary>批量添加设备</summary>
    public async Task<(int successCount, int failedCount)> AddDevicesAsync(List<CloudDeviceInfo> devices)
    {
        var deviceList = new JsonArray(devices.Select(d =>
            (JsonNode)new JsonObject
            {
                ["deviceName"] = d.DeviceName,
                ["udid"] = d.Udid,
                ["deviceType"] = d.DeviceType
            }).ToArray());
        var result = await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v2/device",
            new JsonObject { ["deviceList"] = deviceList });

        var success = result.TryGetProperty("successedCount", out var sc) ? sc.GetInt32() : 0;
        var failed = result.TryGetProperty("failedCount", out var fc) ? fc.GetInt32() : 0;
        return (success, failed);
    }

    /// <summary>查询设备列表</summary>
    public async Task<(List<CloudDeviceInfo> devices, int totalCount)> GetDeviceListAsync(string? deviceName = null, int page = 1, int pageSize = 100)
    {
        var url = $"{_baseUrl}/publish/v2/device/list?fromRecCount={page}&maxReqCount={pageSize}&order=1";
        if (!string.IsNullOrEmpty(deviceName))
            url += $"&deviceName={Uri.EscapeDataString(deviceName)}";

        var result = await SendAsync(HttpMethod.Get, url);

        var devices = new List<CloudDeviceInfo>();
        if (result.TryGetProperty("deviceList", out var dl))
        {
            foreach (var item in dl.EnumerateArray())
                devices.Add(ParseDeviceInfo(item));
        }
        var totalCount = result.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : devices.Count;
        return (devices, totalCount);
    }

    /// <summary>删除设备</summary>
    public async Task DeleteDevicesAsync(List<string> deviceIds) =>
        await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v2/device/delete",
            new JsonObject { ["deviceIds"] = new JsonArray(deviceIds.Select(d => (JsonNode)d).ToArray()) });

    private static CloudDeviceInfo ParseDeviceInfo(JsonElement el)
    {
        return new CloudDeviceInfo
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            DeviceName = el.TryGetProperty("deviceName", out var dn) ? dn.GetString() ?? "" : "",
            Udid = el.TryGetProperty("udid", out var u) ? u.GetString() ?? "" : "",
            DeviceType = el.TryGetProperty("deviceType", out var dt) ? dt.GetInt32() : 4,
            CreateTime = el.TryGetProperty("createTime", out var ct) ? ct.GetString() ?? "" : "",
        };
    }

    #endregion

    #region Profile管理 (AGC Provisioning API v3)

    /// <summary>申请Profile</summary>
    public async Task<CloudProfileInfo> CreateProfileAsync(string provisionName, int provisionType,
        string certId, string appId, List<string>? deviceIdList = null, List<string>? aclPermissionList = null)
    {
        var body = new JsonObject
        {
            ["provisionName"] = provisionName,
            ["provisionType"] = provisionType,
            ["certId"] = certId,
            ["appId"] = appId,
        };
        if (deviceIdList != null && deviceIdList.Count > 0)
            body["deviceIdList"] = new JsonArray(deviceIdList.Select(d => (JsonNode)d).ToArray());
        if (aclPermissionList != null && aclPermissionList.Count > 0)
            body["aclPermissionList"] = new JsonArray(aclPermissionList.Select(p => (JsonNode)p).ToArray());

        var result = await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v3/provision", body);

        var provisionInfo = result.TryGetProperty("provisionInfo", out var pi) ? pi : default;
        if (provisionInfo.ValueKind == JsonValueKind.Undefined)
            throw new Exception("创建Profile失败: 未返回Profile信息");

        return ParseProfileInfo(provisionInfo);
    }

    /// <summary>查询Profile列表</summary>
    public async Task<(List<CloudProfileInfo> profiles, int totalCount)> GetProfileListAsync(
        string? appId = null, string? provisionId = null, int page = 1, int pageSize = 100)
    {
        var url = $"{_baseUrl}/publish/v3/provision/list?fromRecCount={page}&maxReqCount={pageSize}";

        var req = CreateRequest(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(appId))
            req.Headers.Add("appId", appId);
        if (!string.IsNullOrEmpty(provisionId))
            req.Headers.Add("provisionId", provisionId);

        _logService.LogRequest("GET", url);
        using var resp = await _http.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logService.Error($"HTTP {resp.StatusCode}: GET {url}", "Network");
            throw new Exception("登录信息过期或无效");
        }

        var json = await resp.Content.ReadAsStringAsync();
        _logService.LogResponse("GET", url, (int)resp.StatusCode, json);

        if (string.IsNullOrWhiteSpace(json))
            throw new Exception($"AGC接口返回空响应: GET {url}");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new Exception($"AGC接口返回非JSON响应: {json[..Math.Min(json.Length, 200)]}");
        }
        using var _doc = doc;
        var root = doc.RootElement;

        if (root.TryGetProperty("ret", out var ret))
        {
            var code = ret.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            if (code != 0)
            {
                var msg = ret.TryGetProperty("msg", out var m) ? m.GetString() : "请求失败";
                throw new Exception($"AGC错误[{code}]: {msg}");
            }
        }

        var profiles = new List<CloudProfileInfo>();
        if (root.TryGetProperty("provisionList", out var pl))
        {
            foreach (var item in pl.EnumerateArray())
                profiles.Add(ParseProfileInfo(item));
        }
        var totalCount = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : profiles.Count;
        return (profiles, totalCount);
    }

    /// <summary>删除Profile</summary>
    public async Task DeleteProfilesAsync(List<string> provisionIds) =>
        await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v3/provision/delete",
            new JsonObject { ["provisionIds"] = new JsonArray(provisionIds.Select(p => (JsonNode)p).ToArray()) });

    /// <summary>修改Profile绑定的设备</summary>
    public async Task UpdateProfileDevicesAsync(string provisionId, List<string> deviceIdList) =>
        await SendAsync(HttpMethod.Post, $"{_baseUrl}/publish/v3/provision/device",
            new JsonObject
            {
                ["provisionId"] = provisionId,
                ["deviceIdList"] = new JsonArray(deviceIdList.Select(d => (JsonNode)d).ToArray())
            });

    private static CloudProfileInfo ParseProfileInfo(JsonElement el)
    {
        var info = new CloudProfileInfo
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            ProvisionName = el.TryGetProperty("provisionName", out var pn) ? pn.GetString() ?? "" : "",
            ProvisionType = el.TryGetProperty("provisionType", out var pt) ? pt.GetInt32() : 0,
            CertName = el.TryGetProperty("certName", out var cn) ? cn.GetString() ?? "" : "",
            CertId = el.TryGetProperty("certId", out var ci) ? ci.GetString() ?? "" : "",
            ProvisionDownloadUrl = el.TryGetProperty("provisionDownloadUrl", out var pdu) ? pdu.GetString() ?? "" : "",
            UpdateTime = el.TryGetProperty("updateTime", out var ut) ? ut.GetInt64() : 0,
            ExpireTime = el.TryGetProperty("expireTime", out var et) ? et.GetInt64() : 0,
            AppId = el.TryGetProperty("appId", out var ai) ? ai.GetString() ?? "" : "",
            AclPermissionAuditState = el.TryGetProperty("aclPermissionAuditState", out var apas) ? apas.GetInt32() : 0,
        };

        if (el.TryGetProperty("deviceList", out var dl))
        {
            foreach (var d in dl.EnumerateArray())
                info.DeviceList.Add(ParseDeviceInfo(d));
        }

        if (el.TryGetProperty("aclPermissionList", out var apl))
        {
            foreach (var p in apl.EnumerateArray())
            {
                var perm = p.GetString();
                if (!string.IsNullOrEmpty(perm))
                    info.AclPermissionList.Add(perm);
            }
        }

        return info;
    }

    #endregion

    #region 文件下载

    public async Task DownloadFileAsync(string url, string savePath)
    {
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(savePath, bytes);
    }

    #endregion

    #region AppId管理

    /// <summary>查询应用包名对应的AppId (Publishing API v2)</summary>
    public async Task<List<AppIdInfo>> GetAppIdByPackageNameAsync(string packageName)
    {
        var url = $"{_baseUrl}/publish/v2/appid-list?packageName={Uri.EscapeDataString(packageName)}";
        var result = await SendAsync(HttpMethod.Get, url);

        var appIds = new List<AppIdInfo>();
        if (result.TryGetProperty("appids", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var key = item.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                var value = item.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                appIds.Add(new AppIdInfo { PackageName = key, AppId = value });
            }
        }
        return appIds;
    }

    #endregion

    #region 工具方法

    public static string[] GetAclFromModuleJson(string? moduleJson)
    {
        if (string.IsNullOrEmpty(moduleJson)) return Array.Empty<string>();
        try
        {
            var doc = JsonDocument.Parse(moduleJson);
            if (!doc.RootElement.TryGetProperty("module", out var module)) return Array.Empty<string>();
            if (!module.TryGetProperty("requestPermissions", out var perms)) return Array.Empty<string>();

            var permNames = perms.EnumerateArray()
                .Where(p => p.TryGetProperty("name", out var n))
                .Select(p => p.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet();

            return AclList.Where(acl => permNames.Contains(acl)).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    #endregion
}
