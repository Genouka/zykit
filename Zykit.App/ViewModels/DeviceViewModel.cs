using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

/// <summary>统一设备显示项</summary>
public class UnifiedDeviceItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Udid { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public bool IsCloud { get; set; }
    public bool IsLocal { get; set; }
    public string SourceIcon => (IsCloud, IsLocal) switch { (true, true) => "☁📁", (true, false) => "☁", (false, true) => "📁", _ => "" };
    public string SourceTooltip => (IsCloud, IsLocal) switch { (true, true) => "云端+本地", (true, false) => "仅云端", (false, true) => "仅本地", _ => "" };
    public CloudDeviceInfo? CloudDevice { get; set; }
    public DeviceInfo? LocalDevice { get; set; }
}

public partial class DeviceViewModel : ViewModelBase
{
    private readonly HdcService _hdcService;
    private readonly EcoService _ecoService;
    private readonly AuthService _authService;

    // 统一设备列表
    [ObservableProperty] private ObservableCollection<UnifiedDeviceItem> _unifiedDevices = new();
    [ObservableProperty] private UnifiedDeviceItem? _selectedDevice;
    [ObservableProperty] private bool _isLoading = false;

    // WiFi连接
    [ObservableProperty] private string _wifiIp1 = "192";
    [ObservableProperty] private string _wifiIp2 = "168";
    [ObservableProperty] private string _wifiIp3 = "";
    [ObservableProperty] private string _wifiIp4 = "";
    [ObservableProperty] private string _wifiPort = "5555";
    [ObservableProperty] private bool _isConnecting = false;

    // 注册设备表单参数
    [ObservableProperty] private string _newDeviceName = "";
    [ObservableProperty] private string _newDeviceUdid = "";
    [ObservableProperty] private int _newDeviceType = 4; // 4=其他(默认)
    [ObservableProperty] private bool _isRegistering = false;
    [ObservableProperty] private string _operationStatus = "";
    [ObservableProperty] private string _registerFormHint = "连接设备后点击「从设备获取」自动填充UDID";

    // 本地设备详情
    [ObservableProperty] private string _localDeviceInfo = "请先连接设备";

    public string FullWifiAddress => $"{WifiIp1}.{WifiIp2}.{WifiIp3}.{WifiIp4}:{WifiPort}";

    public DeviceViewModel(HdcService hdcService, EcoService ecoService, AuthService authService)
    {
        _hdcService = hdcService;
        _ecoService = ecoService;
        _authService = authService;

        _authService.LoginSuccess += OnLoginSuccess;
    }

    [RelayCommand]
    private async Task RefreshUnifiedListAsync()
    {
        try
        {
            IsLoading = true;
            var localDevices = await _hdcService.GetDeviceListVerboseAsync();
            var (cloudDevices, _) = _ecoService.IsAuthenticated
                ? await _ecoService.GetDeviceListAsync(pageSize: 100)
                : (new List<CloudDeviceInfo>(), 0);

            UnifiedDevices.Clear();
            var cloudByUdid = cloudDevices.Where(d => !string.IsNullOrEmpty(d.Udid))
                .ToDictionary(d => d.Udid, d => d);

            foreach (var ld in localDevices)
            {
                string udid = ld.Udid;
                if (string.IsNullOrEmpty(udid))
                {
                    try { udid = await _hdcService.GetUdidAsync(ld.DeviceId); ld.Udid = udid; } catch { }
                }

                var item = new UnifiedDeviceItem
                {
                    Id = ld.DeviceId, Name = ld.DisplayName, Udid = udid,
                    TypeName = ld.IsWifi ? "WiFi" : "USB", StatusDisplay = ld.Status,
                    IsLocal = true, LocalDevice = ld,
                };

                if (!string.IsNullOrEmpty(udid) && cloudByUdid.TryGetValue(udid, out var cd))
                {
                    item.IsCloud = true;
                    item.CloudDevice = cd;
                    item.Name = cd.DeviceName;
                    item.TypeName = $"{(ld.IsWifi ? "WiFi" : "USB")} / {cd.DeviceTypeName}";
                    cloudByUdid.Remove(udid);
                }
                UnifiedDevices.Add(item);
            }

            foreach (var cd in cloudByUdid.Values)
            {
                UnifiedDevices.Add(new UnifiedDeviceItem
                {
                    Id = cd.Id, Name = cd.DeviceName, Udid = cd.Udid,
                    TypeName = cd.DeviceTypeName, StatusDisplay = "已注册",
                    IsCloud = true, CloudDevice = cd,
                });
            }

            OperationStatus = $"共 {UnifiedDevices.Count} 个设备";
        }
        catch (Exception ex) { OperationStatus = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ConnectWifiAsync()
    {
        if (string.IsNullOrEmpty(WifiIp3) || string.IsNullOrEmpty(WifiIp4)) { OperationStatus = "请输入完整IP"; return; }
        try
        {
            IsConnecting = true;
            var (success, msg) = await _hdcService.ConnectDeviceAsync(FullWifiAddress);
            OperationStatus = success ? "连接成功" : $"连接失败: {msg}";
            if (success) await RefreshUnifiedListAsync();
        }
        catch (Exception ex) { OperationStatus = $"连接失败: {ex.Message}"; }
        finally { IsConnecting = false; }
    }

    [RelayCommand]
    private async Task DisconnectWifiAsync()
    {
        if (SelectedDevice?.LocalDevice == null || !SelectedDevice.LocalDevice.IsWifi) return;
        try
        {
            var (success, msg) = await _hdcService.DisconnectDeviceAsync(SelectedDevice.LocalDevice.DeviceId);
            OperationStatus = success ? "已断开" : $"断开失败: {msg}";
            if (success) await RefreshUnifiedListAsync();
        }
        catch (Exception ex) { OperationStatus = $"断开失败: {ex.Message}"; }
    }

    partial void OnSelectedDeviceChanged(UnifiedDeviceItem? value)
    {
        if (value?.LocalDevice != null)
        {
            LocalDeviceInfo = $"设备: {value.LocalDevice.DeviceId}\nUDID: {value.Udid}\n类型: {value.TypeName}";
            if (!string.IsNullOrEmpty(value.Udid)) NewDeviceUdid = value.Udid;
        }
        else if (value?.CloudDevice != null)
        {
            LocalDeviceInfo = $"云端设备: {value.CloudDevice.DeviceName}\nUDID: {value.Udid}\n类型: {value.TypeName}";
        }
    }

    #region 注册设备（带参数表单）

    /// <summary>从已连接的USB设备自动获取UDID</summary>
    [RelayCommand]
    private async Task FetchUdidFromDeviceAsync()
    {
        try
        {
            var devices = await _hdcService.GetDeviceListVerboseAsync();
            if (devices.Count == 0)
            {
                RegisterFormHint = "未发现已连接设备，请通过USB连接设备并开启开发者模式";
                return;
            }

            var device = devices[0];
            var udid = device.Udid;
            if (string.IsNullOrEmpty(udid))
            {
                try { udid = await _hdcService.GetUdidAsync(device.DeviceId); } catch { }
            }

            if (string.IsNullOrEmpty(udid))
            {
                RegisterFormHint = "无法获取设备UDID，请确保已开启开发者模式和USB调试";
                return;
            }

            NewDeviceUdid = udid;
            if (string.IsNullOrEmpty(NewDeviceName))
                NewDeviceName = $"zykit-{udid[..Math.Min(10, udid.Length)]}";
            RegisterFormHint = $"已从设备 {device.DeviceId} 获取UDID";
        }
        catch (Exception ex)
        {
            RegisterFormHint = $"获取失败: {ex.Message}，请确保设备已通过USB连接";
        }
    }

    /// <summary>检查UDID是否已在云端注册</summary>
    [RelayCommand]
    private async Task CheckDeviceRegisteredAsync()
    {
        if (string.IsNullOrEmpty(NewDeviceUdid)) { RegisterFormHint = "请先输入或获取UDID"; return; }
        if (!_ecoService.IsAuthenticated) { RegisterFormHint = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）"; return; }

        try
        {
            var (cloudDevices, _) = await _ecoService.GetDeviceListAsync(pageSize: 100);
            var existing = cloudDevices.FirstOrDefault(d => d.Udid == NewDeviceUdid);
            if (existing != null)
            {
                RegisterFormHint = $"该设备已在云端注册，名称: {existing.DeviceName}，无需重复注册";
            }
            else
            {
                RegisterFormHint = "该设备尚未在云端注册，可以点击注册";
            }
        }
        catch (Exception ex) { RegisterFormHint = $"检查失败: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task RegisterDeviceAsync()
    {
        if (!_ecoService.IsAuthenticated) { OperationStatus = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）"; return; }
        if (string.IsNullOrEmpty(NewDeviceUdid)) { OperationStatus = "请输入设备UDID"; return; }
        try
        {
            IsRegistering = true;
            var name = string.IsNullOrEmpty(NewDeviceName)
                ? $"zykit-{NewDeviceUdid[..Math.Min(10, NewDeviceUdid.Length)]}"
                : NewDeviceName;
            var (success, failed) = await _ecoService.AddDevicesAsync(
                new List<CloudDeviceInfo> { new() { DeviceName = name, Udid = NewDeviceUdid, DeviceType = NewDeviceType } });
            OperationStatus = failed > 0 ? $"注册部分失败" : $"注册成功: {name}";
            RegisterFormHint = failed > 0 ? "注册失败，该设备可能已注册" : "设备注册成功，现在可以创建包含此设备的Profile";
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"注册失败: {ex.Message}";
            RegisterFormHint = $"注册失败: {ex.Message}，请检查UDID是否正确";
        }
        finally { IsRegistering = false; }
    }

    #endregion

    [RelayCommand]
    private async Task DeleteDeviceAsync()
    {
        if (SelectedDevice == null) return;
        try
        {
            if (SelectedDevice.IsCloud && _ecoService.IsAuthenticated && SelectedDevice.CloudDevice != null)
                await _ecoService.DeleteDevicesAsync(new List<string> { SelectedDevice.CloudDevice.Id });
            OperationStatus = $"已删除: {SelectedDevice.Name}";
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex) { OperationStatus = $"删除失败: {ex.Message}"; }
    }

    private void OnLoginSuccess(AuthInfo auth)
    {
        _ = RefreshUnifiedListAsync();
    }
}
