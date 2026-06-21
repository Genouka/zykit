using System;

namespace Zykit.App.Models;

public class LocalCacheEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "cert", "profile", "keystore", "device"
    public string LocalPath { get; set; } = "";
    public string CloudId { get; set; } = "";
    public string PackageName { get; set; } = "";
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public string StatusDisplay => IsExpired ? "已过期" : "有效";
}
