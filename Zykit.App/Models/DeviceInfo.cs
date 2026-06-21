namespace Zykit.App.Models;

public partial class DeviceInfo
{
    public string DeviceId { get; set; } = "";
    public string Udid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsWifi => DeviceId.Contains(':');
    public bool IsUsb => !DeviceId.Contains(':');
    public string Status { get; set; } = ""; // Connected, Unauthorized, Offline
    public string DisplayName => string.IsNullOrEmpty(Name)
        ? (IsWifi ? $"WiFi: {DeviceId}" : $"USB: {DeviceId}")
        : $"{Name} ({(IsWifi ? "WiFi" : "USB")})";
}
