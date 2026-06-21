using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

/// <summary>统一Profile显示项</summary>
public class UnifiedProfileItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string AppId { get; set; } = "";
    public string CertName { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public string ExpiresDisplay => ExpiresAt?.ToString("yyyy-MM-dd") ?? "永久";
    public bool IsCloud { get; set; }
    public bool IsLocal { get; set; }
    public string SourceIcon => (IsCloud, IsLocal) switch { (true, true) => "☁📁", (true, false) => "☁", (false, true) => "📁", _ => "" };
    public string SourceTooltip => (IsCloud, IsLocal) switch { (true, true) => "云端+本地", (true, false) => "仅云端", (false, true) => "仅本地", _ => "" };
    public string CloudId { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public CloudProfileInfo? CloudProfile { get; set; }
    public LocalCacheEntry? LocalEntry { get; set; }
}

public partial class ProfileViewModel : ViewModelBase
{
    private readonly EcoService _ecoService;
    private readonly HdcService _hdcService;
    private readonly LocalCacheService _cacheService;
    private readonly PairingService _pairingService;
    private readonly ISdkPathProvider _sdk;
    private readonly AuthService _authService;

    // 统一Profile列表
    [ObservableProperty] private ObservableCollection<UnifiedProfileItem> _unifiedProfiles = new();
    [ObservableProperty] private UnifiedProfileItem? _selectedProfile;
    [ObservableProperty] private bool _isLoading = false;

    // 创建Profile表单参数
    [ObservableProperty] private string _provisionName = "";
    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private string _appId = "";
    [ObservableProperty] private int _provisionType = 1; // 1=调试
    [ObservableProperty] private ObservableCollection<CloudCertInfo> _cloudCerts = new();
    [ObservableProperty] private CloudCertInfo? _selectedCert;
    [ObservableProperty] private ObservableCollection<CloudDeviceInfo> _cloudDevices = new();
    [ObservableProperty] private ObservableCollection<CloudDeviceInfo> _selectedDevices = new();
    [ObservableProperty] private ObservableCollection<string> _availableAclPermissions = new();
    [ObservableProperty] private ObservableCollection<string> _selectedAclPermissions = new();
    [ObservableProperty] private bool _isCreating = false;
    [ObservableProperty] private string _profileFormHint = "请先加载云端资源，然后填写参数创建Profile";
    [ObservableProperty] private ObservableCollection<AppIdInfo> _appIds = new();
    [ObservableProperty] private AppIdInfo? _selectedAppId;

    // 签名搭配
    [ObservableProperty] private ObservableCollection<SigningPair> _signingPairs = new();
    [ObservableProperty] private SigningPair? _selectedPair;

    [ObservableProperty] private string _operationStatus = "";

    // UI状态
    [ObservableProperty] private bool _showCreateProfileForm = false;

    public ProfileViewModel(EcoService ecoService, HdcService hdcService,
        LocalCacheService cacheService, PairingService pairingService, ISdkPathProvider sdk,
        AuthService authService)
    {
        _ecoService = ecoService;
        _hdcService = hdcService;
        _cacheService = cacheService;
        _pairingService = pairingService;
        _sdk = sdk;
        _authService = authService;

        _authService.LoginSuccess += OnLoginSuccess;
        foreach (var acl in EcoService.AclList) AvailableAclPermissions.Add(acl);
        RefreshSigningPairs();
        RefreshLocalOnly();
    }

    private void RefreshLocalOnly()
    {
        var locals = _cacheService.GetEntriesByType("profile");
        UnifiedProfiles.Clear();
        foreach (var local in locals)
        {
            UnifiedProfiles.Add(new UnifiedProfileItem
            {
                Id = local.Id, Name = local.Name, TypeName = "本地Profile",
                PackageName = local.PackageName, StatusDisplay = local.StatusDisplay,
                ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                IsLocal = true, LocalPath = local.LocalPath, LocalEntry = local,
            });
        }
    }

    private void RefreshSigningPairs()
    {
        SigningPairs.Clear();
        foreach (var p in _pairingService.GetAllPairs()) SigningPairs.Add(p);
    }

    [RelayCommand]
    private async Task RefreshUnifiedListAsync()
    {
        try
        {
            IsLoading = true;
            var localEntries = _cacheService.GetEntriesByType("profile");
            var (cloudProfiles, _) = _ecoService.IsAuthenticated
                ? await _ecoService.GetProfileListAsync(pageSize: 100)
                : (new List<CloudProfileInfo>(), 0);

            UnifiedProfiles.Clear();
            var localByCloudId = localEntries.Where(e => !string.IsNullOrEmpty(e.CloudId))
                .ToDictionary(e => e.CloudId, e => e);

            foreach (var cp in cloudProfiles)
            {
                var item = new UnifiedProfileItem
                {
                    Id = cp.Id, Name = cp.ProvisionName, TypeName = cp.ProvisionTypeName,
                    AppId = cp.AppId, CertName = cp.CertName, StatusDisplay = cp.StatusDisplay,
                    ExpiresAt = cp.ExpireTime > 0 ? cp.ExpireTimeUtc : null,
                    IsCloud = true, CloudId = cp.Id, DownloadUrl = cp.ProvisionDownloadUrl,
                    CloudProfile = cp,
                };
                if (localByCloudId.TryGetValue(cp.Id, out var local))
                {
                    item.IsLocal = true;
                    item.LocalPath = local.LocalPath;
                    item.LocalEntry = local;
                    item.PackageName = local.PackageName;
                    localByCloudId.Remove(cp.Id);
                }
                UnifiedProfiles.Add(item);
            }

            foreach (var local in localByCloudId.Values)
            {
                UnifiedProfiles.Add(new UnifiedProfileItem
                {
                    Id = local.Id, Name = local.Name, TypeName = "本地Profile",
                    PackageName = local.PackageName, StatusDisplay = local.StatusDisplay,
                    ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                    IsLocal = true, LocalPath = local.LocalPath, LocalEntry = local,
                });
            }

            OperationStatus = $"共 {UnifiedProfiles.Count} 个Profile";
        }
        catch (Exception ex) { OperationStatus = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    /// <summary>加载云端证书和设备列表供创建表单选择</summary>
    [RelayCommand]
    private async Task LoadCloudResourcesAsync()
    {
        if (!_ecoService.IsAuthenticated) { ProfileFormHint = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）"; return; }
        try
        {
            var certs = await _ecoService.GetCertListAsync(certType: 1);
            CloudCerts.Clear();
            foreach (var c in certs) CloudCerts.Add(c);

            var (devices, _) = await _ecoService.GetDeviceListAsync(pageSize: 100);
            CloudDevices.Clear();
            foreach (var d in devices) CloudDevices.Add(d);

            // 自动选择第一个有效证书
            if (SelectedCert == null && CloudCerts.Count > 0)
                SelectedCert = CloudCerts.FirstOrDefault(c => !c.IsExpired) ?? CloudCerts[0];

            ProfileFormHint = $"已加载 {certs.Count} 个证书, {devices.Count} 个设备";
            if (certs.Count == 0)
                ProfileFormHint = "没有可用的调试证书，请先到签名管理页面创建证书";
        }
        catch (Exception ex) { ProfileFormHint = $"加载失败: {ex.Message}"; }
    }

    /// <summary>从已连接设备获取UDID并自动选中对应云端设备</summary>
    [RelayCommand]
    private async Task FetchDeviceFromUsbAsync()
    {
        try
        {
            var devices = await _hdcService.GetDeviceListVerboseAsync();
            if (devices.Count == 0)
            {
                ProfileFormHint = "未发现已连接设备，请通过USB连接设备";
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
                ProfileFormHint = "无法获取设备UDID，请确保已开启开发者模式";
                return;
            }

            // 自动选中云端设备
            var cloudDevice = CloudDevices.FirstOrDefault(d => d.Udid == udid);
            if (cloudDevice != null)
            {
                if (!SelectedDevices.Contains(cloudDevice))
                    SelectedDevices.Add(cloudDevice);
                ProfileFormHint = $"已自动选中设备: {cloudDevice.DeviceName}";
            }
            else
            {
                ProfileFormHint = $"设备UDID: {udid}，但该设备尚未在云端注册，请先到设备管理页面注册";
            }
        }
        catch (Exception ex) { ProfileFormHint = $"获取设备失败: {ex.Message}"; }
    }

    /// <summary>自动填充Profile创建表单默认值</summary>
    [RelayCommand]
    private void AutoFillProfileForm()
    {
        if (string.IsNullOrEmpty(ProvisionName) && !string.IsNullOrEmpty(PackageName))
            ProvisionName = $"zykit-{PackageName}";
        ProvisionType = 1;
        ProfileFormHint = "已填入推荐默认值";
    }

    /// <summary>查询包名对应的AppId</summary>
    [RelayCommand]
    private async Task QueryAppIdAsync()
    {
        if (!_ecoService.IsAuthenticated) { ProfileFormHint = "请先登录华为账号"; return; }
        if (string.IsNullOrEmpty(PackageName)) { ProfileFormHint = "请先输入包名"; return; }
        try
        {
            var result = await _ecoService.GetAppIdByPackageNameAsync(PackageName);
            AppIds.Clear();
            foreach (var item in result) AppIds.Add(item);
            if (AppIds.Count > 0)
            {
                AppId = AppIds[0].AppId;
                SelectedAppId = AppIds[0];
                ProfileFormHint = $"查询到 {AppIds.Count} 个AppId，已自动填入第一个";
            }
            else
            {
                ProfileFormHint = "未查询到AppId，请确认包名是否正确，或前往AGC创建应用";
            }
        }
        catch (Exception ex) { ProfileFormHint = $"查询AppId失败: {ex.Message}"; }
    }

    /// <summary>前往AGC创建应用</summary>
    [RelayCommand]
    private void OpenCreateAppUrl()
    {
        Process.Start(new ProcessStartInfo("https://developer.huawei.com/consumer/cn/service/jsp/agc.html") { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        if (!_ecoService.IsAuthenticated) { OperationStatus = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）"; return; }
        if (SelectedCert == null) { OperationStatus = "请选择证书（点击加载资源按钮获取证书列表）"; return; }
        if (string.IsNullOrEmpty(PackageName)) { OperationStatus = "请输入包名"; return; }

        try
        {
            IsCreating = true;
            OperationStatus = "创建Profile...";
            var name = string.IsNullOrEmpty(ProvisionName) ? $"zykit-{PackageName}" : ProvisionName;
            var deviceIds = SelectedDevices.Select(d => d.Id).ToList();
            var aclPerms = SelectedAclPermissions.ToList();

            var profile = await _ecoService.CreateProfileAsync(name, ProvisionType, SelectedCert.Id, AppId,
                deviceIds, aclPerms);

            if (!string.IsNullOrEmpty(profile.ProvisionDownloadUrl))
            {
                OperationStatus = "下载Profile文件...";
                var localPath = _cacheService.GetProfileCachePath(profile.Id, PackageName);
                await _ecoService.DownloadFileAsync(profile.ProvisionDownloadUrl, localPath);
                profile.LocalPath = localPath;
                _cacheService.CacheProfile(profile, localPath, PackageName);
                OperationStatus = $"Profile创建成功: {name}";
                ProfileFormHint = "Profile已创建并下载到本地，现在可以用于签名安装";
            }
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"创建失败: {ex.Message}";
            ProfileFormHint = $"创建失败: {ex.Message}，请检查参数是否正确";
        }
        finally { IsCreating = false; }
    }

    [RelayCommand]
    private async Task DownloadProfileAsync()
    {
        if (SelectedProfile == null || string.IsNullOrEmpty(SelectedProfile.DownloadUrl)) return;
        try
        {
            // 缓存键优先使用 AppId（云端 Profile 标识），本地创建的回退到 PackageName
            var cacheKey = !string.IsNullOrEmpty(SelectedProfile.AppId) ? SelectedProfile.AppId : SelectedProfile.PackageName;
            var localPath = _cacheService.GetProfileCachePath(SelectedProfile.CloudId, cacheKey);
            await _ecoService.DownloadFileAsync(SelectedProfile.DownloadUrl, localPath);
            if (SelectedProfile.CloudProfile != null)
                _cacheService.CacheProfile(SelectedProfile.CloudProfile, localPath, cacheKey);
            await RefreshUnifiedListAsync();
            OperationStatus = "Profile已下载到本地";
        }
        catch (Exception ex) { OperationStatus = $"下载失败: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null) return;
        try
        {
            if (SelectedProfile.IsCloud && _ecoService.IsAuthenticated)
                await _ecoService.DeleteProfilesAsync(new List<string> { SelectedProfile.CloudId });
            if (SelectedProfile.IsLocal && SelectedProfile.LocalEntry != null)
            {
                if (System.IO.File.Exists(SelectedProfile.LocalPath)) try { System.IO.File.Delete(SelectedProfile.LocalPath); } catch { }
                _cacheService.RemoveEntry(SelectedProfile.LocalEntry.Id);
            }
            OperationStatus = $"已删除: {SelectedProfile.Name}";
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex) { OperationStatus = $"删除失败: {ex.Message}"; }
    }

    [RelayCommand]
    private void ClearExpired()
    {
        _cacheService.ClearExpired();
        _pairingService.ClearExpiredPairs();
        RefreshSigningPairs();
        RefreshLocalOnly();
        OperationStatus = "已清理过期缓存";
    }

    [RelayCommand]
    private void DeletePair()
    {
        if (SelectedPair == null) return;
        _pairingService.DeletePair(SelectedPair.Id);
        RefreshSigningPairs();
    }

    private void OnLoginSuccess(AuthInfo auth)
    {
        _ = RefreshUnifiedListAsync();
    }
}
