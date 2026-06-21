using System;

namespace Zykit.App.Models;

public class AuthInfo
{
    public string AccessToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public string NickName { get; set; } = "";
    public string TeamId { get; set; } = "";
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    public int ExpiresIn { get; set; } = 86400; // 默认24小时

    public DateTime ExpiresAt => LoginTime.AddSeconds(ExpiresIn);
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}
