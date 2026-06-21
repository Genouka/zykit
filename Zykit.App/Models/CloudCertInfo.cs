using System;

namespace Zykit.App.Models;

public class CloudCertInfo
{
    public string Id { get; set; } = "";
    public string CertName { get; set; } = "";
    public int CertType { get; set; }
    public string CertTypeName => CertType switch { 1 => "调试证书", 2 => "发布证书", 3 => "In-house发布证书", 4 => "二进制证书", _ => $"未知({CertType})" };
    public long CreateTime { get; set; }
    public long ExpireTime { get; set; }
    public DateTime CreateTimeUtc => DateTimeOffset.FromUnixTimeMilliseconds(CreateTime).DateTime;
    public DateTime ExpireTimeUtc => DateTimeOffset.FromUnixTimeMilliseconds(ExpireTime).DateTime;
    public string CertDownloadUrl { get; set; } = "";
    public bool IsExpired => ExpireTime > 0 && DateTime.UtcNow > ExpireTimeUtc;
    public string LocalPath { get; set; } = "";
    public string StatusDisplay => IsExpired ? "已过期" : "有效";
}
