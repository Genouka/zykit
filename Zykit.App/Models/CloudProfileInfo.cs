using System;
using System.Collections.Generic;

namespace Zykit.App.Models;

public class CloudProfileInfo
{
    public string Id { get; set; } = "";
    public string ProvisionName { get; set; } = "";
    public int ProvisionType { get; set; }
    public string ProvisionTypeName => ProvisionType switch { 1 => "调试Profile", 2 => "发布Profile", 3 => "In-house发布Profile", 6 => "指定设备发布Profile", _ => $"类型{ProvisionType}" };
    public string CertName { get; set; } = "";
    public string CertId { get; set; } = "";
    public List<CloudDeviceInfo> DeviceList { get; set; } = new();
    public List<string> AclPermissionList { get; set; } = new();
    public int AclPermissionAuditState { get; set; }
    public string ProvisionDownloadUrl { get; set; } = "";
    public long UpdateTime { get; set; }
    public long ExpireTime { get; set; }
    public DateTime ExpireTimeUtc => ExpireTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ExpireTime).DateTime : DateTime.MaxValue;
    public string AppId { get; set; } = "";
    public bool IsExpired => ExpireTime > 0 && DateTime.UtcNow > ExpireTimeUtc;
    public string LocalPath { get; set; } = "";
    public string StatusDisplay => IsExpired ? "已过期" : "有效";
}
