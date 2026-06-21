using System;

namespace Zykit.App.Models;

public class CloudDeviceInfo
{
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Udid { get; set; } = "";
    public int DeviceType { get; set; }
    public string DeviceTypeName => DeviceType switch { 1 => "手机", 2 => "平板", 3 => "手表", 4 => "其他", _ => $"类型{DeviceType}" };
    public string CreateTime { get; set; } = "";
}
