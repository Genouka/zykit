using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class InstallViewModel : ViewModelBase
{
    private readonly BuildService _buildService;
    private readonly HdcService _hdcService;
    private readonly KeyStoreService _keyStoreService;
    private readonly AuthService _authService;
    private readonly EcoService _ecoService;
    private readonly LocalCacheService _cacheService;
    private readonly PairingService _pairingService;

    [ObservableProperty]
    private string _hapFilePath = "";

    [ObservableProperty]
    private string _hapFileName = "未选择文件";

    [ObservableProperty]
    private string _packageName = "";

    [ObservableProperty]
    private string _appName = "";

    [ObservableProperty]
    private ObservableCollection<DeviceInfo> _devices = new();

    [ObservableProperty]
    private DeviceInfo? _selectedDevice;

    [ObservableProperty]
    private string _installStatus = "等待操作...";

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private bool _isInstalling = false;

    [ObservableProperty]
    private int _currentStep = -1;

    [ObservableProperty]
    private bool _showAdvancedOptions = false;

    [ObservableProperty]
    private ObservableCollection<UnifiedCertItem> _availableCerts = new();

    [ObservableProperty]
    private UnifiedCertItem? _selectedCert;

    [ObservableProperty]
    private ObservableCollection<UnifiedProfileItem> _availableProfiles = new();

    [ObservableProperty]
    private UnifiedProfileItem? _selectedProfile;

    [ObservableProperty]
    private bool _isLoadingResources = false;

    partial void OnShowAdvancedOptionsChanged(bool value)
    {
        if (value && AvailableCerts.Count == 0 && AvailableProfiles.Count == 0)
        {
            _ = LoadSigningResourcesAsync();
        }
    }

    public string[] StepNames { get; } = { "账号验证", "创建证书", "创建Profile", "签名应用", "安装应用" };

    public InstallViewModel(BuildService buildService, HdcService hdcService, KeyStoreService keyStoreService, AuthService authService, EcoService ecoService, LocalCacheService cacheService, PairingService pairingService)
    {
        _buildService = buildService;
        _hdcService = hdcService;
        _keyStoreService = keyStoreService;
        _authService = authService;
        _ecoService = ecoService;
        _cacheService = cacheService;
        _pairingService = pairingService;

        _buildService.StepStart += (name, idx) =>
        {
            CurrentStep = idx;
            AppendLog($"[开始] {name}...");
        };
        _buildService.StepFinish += (name, value, msg) => AppendLog($"[完成] {name}");
        _buildService.StepError += (name, error, msg) => AppendLog($"[错误] {name}: {error}");
        _buildService.Log += msg => AppendLog(msg);
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        try
        {
            var devices = await _hdcService.GetDeviceListAsync();
            Devices.Clear();
            foreach (var d in devices)
                Devices.Add(d);
            if (Devices.Count > 0)
                SelectedDevice = Devices[0];
        }
        catch (Exception ex)
        {
            AppendLog($"刷新设备失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowseHapFileAsync()
    {
        // File picker needs to be called from the View, so we use a simple approach
        // The View will handle the actual file dialog and call SetHapFile
        // For now, this is a placeholder
    }

    public void SetHapFile(string filePath)
    {
        HapFilePath = filePath;
        HapFileName = System.IO.Path.GetFileName(filePath);

        var hapInfo = _keyStoreService.LoadHapInfo(filePath);
        if (hapInfo != null)
        {
            PackageName = hapInfo.PackageName;
            AppName = hapInfo.AppName;
        }
    }

    [RelayCommand]
    private async Task LoadSigningResourcesAsync()
    {
        if (!_ecoService.IsAuthenticated) return;

        try
        {
            IsLoadingResources = true;

            // 加载证书列表
            var localCertEntries = _cacheService.GetEntriesByType("cert");
            var cloudCerts = await _ecoService.GetCertListAsync();
            AvailableCerts.Clear();
            var localByCloudId = localCertEntries.Where(e => !string.IsNullOrEmpty(e.CloudId))
                .ToDictionary(e => e.CloudId, e => e);

            foreach (var cc in cloudCerts)
            {
                var item = new UnifiedCertItem
                {
                    Id = cc.Id, Name = cc.CertName, TypeName = cc.CertTypeName,
                    StatusDisplay = cc.StatusDisplay,
                    ExpiresAt = cc.ExpireTime > 0 ? cc.ExpireTimeUtc : null,
                    IsCloud = true, CloudId = cc.Id, DownloadUrl = cc.CertDownloadUrl,
                    CloudCert = cc,
                };
                if (localByCloudId.TryGetValue(cc.Id, out var local))
                {
                    item.IsLocal = true;
                    item.LocalPath = local.LocalPath;
                    item.LocalEntry = local;
                    localByCloudId.Remove(cc.Id);
                }
                AvailableCerts.Add(item);
            }
            foreach (var local in localByCloudId.Values)
            {
                AvailableCerts.Add(new UnifiedCertItem
                {
                    Id = local.Id, Name = local.Name, TypeName = "本地证书",
                    StatusDisplay = local.StatusDisplay,
                    ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                    IsCloud = false, IsLocal = true, CloudId = local.CloudId,
                    LocalPath = local.LocalPath, LocalEntry = local,
                });
            }

            // 加载Profile列表
            var localProfileEntries = _cacheService.GetEntriesByType("profile");
            var (cloudProfiles, _) = await _ecoService.GetProfileListAsync(pageSize: 100);
            AvailableProfiles.Clear();
            var localProfileByCloudId = localProfileEntries.Where(e => !string.IsNullOrEmpty(e.CloudId))
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
                if (localProfileByCloudId.TryGetValue(cp.Id, out var local))
                {
                    item.IsLocal = true;
                    item.LocalPath = local.LocalPath;
                    item.LocalEntry = local;
                    item.PackageName = local.PackageName;
                    localProfileByCloudId.Remove(cp.Id);
                }
                AvailableProfiles.Add(item);
            }
            foreach (var local in localProfileByCloudId.Values)
            {
                AvailableProfiles.Add(new UnifiedProfileItem
                {
                    Id = local.Id, Name = local.Name, TypeName = "本地Profile",
                    PackageName = local.PackageName, StatusDisplay = local.StatusDisplay,
                    ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                    IsLocal = true, LocalPath = local.LocalPath, LocalEntry = local,
                });
            }
        }
        catch (Exception ex)
        {
            AppendLog($"加载签名资源失败: {ex.Message}");
        }
        finally
        {
            IsLoadingResources = false;
        }
    }

    [RelayCommand]
    private async Task OneClickInstallAsync()
    {
        if (string.IsNullOrEmpty(HapFilePath))
        {
            InstallStatus = "请先选择HAP文件";
            return;
        }

        if (!_authService.IsLoggedIn)
        {
            InstallStatus = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）";
            return;
        }

        try
        {
            IsInstalling = true;
            ProgressValue = 0;
            LogText = "";
            CurrentStep = 0;

            var deviceIp = SelectedDevice?.IsWifi == true ? SelectedDevice.DeviceId : null;

            // 获取高级选项覆盖参数
            string? overrideCertId = null, overrideCertPath = null;
            string? overrideProfileId = null, overrideProfilePath = null;
            if (ShowAdvancedOptions)
            {
                if (SelectedCert != null)
                {
                    overrideCertId = SelectedCert.CloudId;
                    overrideCertPath = SelectedCert.LocalPath;
                }
                if (SelectedProfile != null)
                {
                    overrideProfileId = SelectedProfile.CloudId;
                    overrideProfilePath = SelectedProfile.LocalPath;
                }
            }

            // Step 1-3: Check account and prepare
            var prepareSuccess = await _buildService.CheckAccountAndPrepareAsync(PackageName, deviceIp,
                overrideCertId: overrideCertId, overrideCertPath: overrideCertPath,
                overrideProfileId: overrideProfileId, overrideProfilePath: overrideProfilePath);
            if (!prepareSuccess)
            {
                InstallStatus = "准备失败，请检查日志";
                return;
            }

            ProgressValue = 60;

            // Step 4-5: Sign and install
            var (success, message) = await _buildService.SignAndInstallAsync(HapFilePath, deviceIp);
            ProgressValue = 100;

            InstallStatus = success ? "安装完成" : $"失败: {message}";
        }
        catch (Exception ex)
        {
            InstallStatus = $"安装失败: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void AppendLog(string text)
    {
        LogText += text + "\n";
    }
}
