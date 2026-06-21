using System;

namespace Zykit.App.Models;

/// <summary>
/// 签名搭配关系：一个包名对应一组密钥库+证书+Profile
/// </summary>
public class SigningPair
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PackageName { get; set; } = "";
    public string AppId { get; set; } = "";

    // 密钥库
    public string KeystoreName { get; set; } = "AUTO_ZYKIT";
    public string KeystorePath { get; set; } = "";
    public string KeystorePassword { get; set; } = "zykit123";
    public string KeyAlias { get; set; } = "zykit";

    // 证书
    public string CertCloudId { get; set; } = "";
    public string CertLocalPath { get; set; } = "";
    public string CertName { get; set; } = "";

    // Profile
    public string ProfileCloudId { get; set; } = "";
    public string ProfileLocalPath { get; set; } = "";
    public string ProfileName { get; set; } = "";

    // 元数据
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CertExpiresAt { get; set; }
    public DateTime? ProfileExpiresAt { get; set; }

    public bool IsCertValid => !CertExpiresAt.HasValue || DateTime.UtcNow < CertExpiresAt.Value;
    public bool IsProfileValid => !ProfileExpiresAt.HasValue || DateTime.UtcNow < ProfileExpiresAt.Value;
    public bool IsValid => IsCertValid && IsProfileValid
        && !string.IsNullOrEmpty(KeystorePath)
        && !string.IsNullOrEmpty(CertLocalPath)
        && !string.IsNullOrEmpty(ProfileLocalPath);
    public string StatusDisplay => (IsCertValid, IsProfileValid) switch
    {
        (true, true) => "有效",
        (false, true) => "证书过期",
        (true, false) => "Profile过期",
        (false, false) => "均已过期"
    };
}
