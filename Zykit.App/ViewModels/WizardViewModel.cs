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

public partial class WizardViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly HdcService _hdcService;
    private readonly BuildService _buildService;
    private readonly KeyStoreService _keyStoreService;
    private readonly EcoService _ecoService;
    private readonly LocalCacheService _cacheService;
    private readonly PairingService _pairingService;

    #region 步骤跟踪

    [ObservableProperty]
    private int _currentStepIndex = 0;

    public IEnumerable<string> Steps { get; } = new[] { "登录账号", "连接设备", "安装应用" };

    public bool IsStep0 => CurrentStepIndex == 0;
    public bool IsStep1 => CurrentStepIndex == 1;
    public bool IsStep2 => CurrentStepIndex == 2;

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
    }

    partial void OnManualUdidChanged(string value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnIsRealDeviceDebugModeChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    public bool CanGoNext => CurrentStepIndex switch
    {
        0 => IsLoggedIn,
        1 => IsRealDeviceDebugMode
            ? !string.IsNullOrWhiteSpace(ManualUdid)
            : IsDeviceReady,
        _ => false
    };

    public bool CanGoBack => CurrentStepIndex > 0 && !IsInstalling;

    #endregion

    #region 步骤1: 登录账号

    [ObservableProperty]
    private bool _isLoggedIn = false;

    [ObservableProperty]
    private string _loginStatus = "正在检查登录状态...";

    [ObservableProperty]
    private string _userDetail = "";

    [ObservableProperty]
    private bool _isLoggingIn = false;

    [ObservableProperty]
    private string _accountErrorMessage = "";

    #endregion

    #region 步骤2: 连接设备

    [ObservableProperty]
    private ObservableCollection<DeviceInfo> _devices = new();

    [ObservableProperty]
    private DeviceInfo? _selectedDevice;

    [ObservableProperty]
    private string _deviceStatus = "正在检测设备...";

    [ObservableProperty]
    private bool _isDeviceReady = false;

    [ObservableProperty]
    private bool _isDetectingDevice = false;

    [ObservableProperty]
    private string _deviceErrorMessage = "";

    // 设备云端注册检查
    [ObservableProperty]
    private bool _isDeviceRegistered = false;

    [ObservableProperty]
    private bool _isCheckingRegistration = false;

    [ObservableProperty]
    private string _deviceUdid = "";

    [ObservableProperty]
    private string _deviceRegistrationWarning = "";

    [ObservableProperty]
    private bool _isRegisteringDevice = false;

    // WiFi连接（高级选项）
    [ObservableProperty]
    private string _wifiIp1 = "192";

    [ObservableProperty]
    private string _wifiIp2 = "168";

    [ObservableProperty]
    private string _wifiIp3 = "";

    [ObservableProperty]
    private string _wifiIp4 = "";

    [ObservableProperty]
    private string _wifiPort = "5555";

    [ObservableProperty]
    private bool _isConnectingWifi = false;

    [ObservableProperty]
    private bool _showWifiOptions = false;

    public string FullWifiAddress => $"{WifiIp1}.{WifiIp2}.{WifiIp3}.{WifiIp4}:{WifiPort}";

    // 真机调试模式（支持模拟器 UDID，无需物理设备在线）
    [ObservableProperty]
    private bool _isRealDeviceDebugMode = true;

    [ObservableProperty]
    private string _manualUdid = "";

    [ObservableProperty]
    private bool _isFetchingUdid = false;

    [ObservableProperty]
    private string _manualUdidHint = "可手动输入设备或模拟器的 UDID，或点击右侧按钮从已连接设备获取";

    #endregion

    #region 步骤3: 安装应用

    [ObservableProperty]
    private string _hapFilePath = "";

    [ObservableProperty]
    private string _hapFileName = "未选择文件";

    [ObservableProperty]
    private string _packageName = "";

    [ObservableProperty]
    private string _appName = "";

    [ObservableProperty]
    private string _installStatus = "等待操作...";

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private bool _isInstalling = false;

    [ObservableProperty]
    private int _installStepIndex = -1;

    public IEnumerable<string> InstallSteps { get; } = new[] { "账号验证", "创建证书", "创建Profile", "签名应用", "安装应用" };

    [ObservableProperty]
    private bool _installCompleted = false;

    [ObservableProperty]
    private bool _installSuccess = false;

    [ObservableProperty]
    private string _installResultMessage = "";

    [ObservableProperty]
    private string _installErrorMessage = "";

    [ObservableProperty]
    private bool _showAdvancedLog = false;

    // 高级选项：签名和Profile选择
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

    // 搭配关系与适用性
    [ObservableProperty]
    private string _pairingStatus = "";

    [ObservableProperty]
    private bool _hasApplicableCert = true;

    [ObservableProperty]
    private bool _hasApplicableProfile = true;

    [ObservableProperty]
    private bool _isQuickGenerating = false;

    // ACL 权限选择（真机调试）
    [ObservableProperty]
    private ObservableCollection<AclPermissionItem> _aclPermissions = new();

    [ObservableProperty]
    private bool _showAclOptions = false;

    #endregion

    public WizardViewModel(AuthService authService, HdcService hdcService,
        BuildService buildService, KeyStoreService keyStoreService, EcoService ecoService,
        LocalCacheService cacheService, PairingService pairingService)
    {
        _authService = authService;
        _hdcService = hdcService;
        _buildService = buildService;
        _keyStoreService = keyStoreService;
        _ecoService = ecoService;
        _cacheService = cacheService;
        _pairingService = pairingService;

        _authService.LoginSuccess += OnLoginSuccess;
        _authService.LogoutSuccess += OnLogoutSuccess;

        // 初始化 ACL 权限列表（默认全选）
        foreach (var acl in EcoService.AclList)
            AclPermissions.Add(new AclPermissionItem { Name = acl, IsSelected = true });

        _buildService.StepStart += (name, idx) =>
        {
            InstallStepIndex = idx;
            AppendLog($"[开始] {name}...");
        };
        _buildService.StepFinish += (name, value, msg) => AppendLog($"[完成] {name}");
        _buildService.StepError += (name, error, msg) => AppendLog($"[错误] {name}: {error}");
        _buildService.Log += msg => AppendLog(msg);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var success = await _authService.TryAutoLoginAsync();
        if (!success)
        {
            LoginStatus = "未登录";
            IsLoggedIn = false;
        }
    }

    #region 登录事件

    private void OnLoginSuccess(AuthInfo auth)
    {
        LoginStatus = $"已登录: {auth.NickName}";
        UserDetail = $"用户ID: {auth.UserId}";
        IsLoggedIn = true;
        AccountErrorMessage = "";
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void OnLogoutSuccess()
    {
        LoginStatus = "未登录";
        UserDetail = "";
        IsLoggedIn = false;
        OnPropertyChanged(nameof(CanGoNext));
    }

    #endregion

    #region 步骤1命令: 登录

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            IsLoggingIn = true;
            AccountErrorMessage = "";
            await _authService.LoginAsync();
        }
        catch (Exception ex)
        {
            AccountErrorMessage = $"登录失败: {ex.Message}\n\n请检查网络连接后重试。如果问题持续，请在左侧菜单「账号管理」中查看详情。";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    #endregion

    #region 步骤2命令: 设备

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        try
        {
            IsDetectingDevice = true;
            DeviceErrorMessage = "";
            DeviceRegistrationWarning = "";
            DeviceStatus = "正在检测设备...";

            var devices = await _hdcService.GetDeviceListVerboseAsync();
            Devices.Clear();
            foreach (var d in devices)
                Devices.Add(d);

            if (Devices.Count > 0)
            {
                SelectedDevice = Devices[0];
                IsDeviceReady = true;
                DeviceStatus = $"已检测到 {Devices.Count} 个设备";
                // 自动检查云端注册状态
                _ = CheckDeviceRegistrationAsync();
            }
            else
            {
                IsDeviceReady = false;
                DeviceStatus = "未检测到设备";
                DeviceErrorMessage = "未检测到已连接的设备，请确认：\n\n1. USB线已连接到电脑\n2. 手机已开启开发者模式\n3. 手机上已允许USB调试\n\n如果使用WiFi连接，请点击下方「无线连接」展开高级选项。";
            }

            OnPropertyChanged(nameof(CanGoNext));
        }
        catch (Exception ex)
        {
            IsDeviceReady = false;
            DeviceStatus = "检测失败";
            DeviceErrorMessage = $"设备检测失败: {ex.Message}\n\n请确认hdc工具可用，可在左侧菜单「设置」中检查SDK路径。";
            OnPropertyChanged(nameof(CanGoNext));
        }
        finally
        {
            IsDetectingDevice = false;
        }
    }

    private async Task CheckDeviceRegistrationAsync()
    {
        if (SelectedDevice == null || !_ecoService.IsAuthenticated) return;

        try
        {
            IsCheckingRegistration = true;
            DeviceRegistrationWarning = "";

            // 获取设备UDID
            var udid = SelectedDevice.Udid;
            if (string.IsNullOrEmpty(udid))
            {
                try { udid = await _hdcService.GetUdidAsync(SelectedDevice.DeviceId); }
                catch { }
            }

            DeviceUdid = udid ?? "";

            if (string.IsNullOrEmpty(udid))
            {
                DeviceRegistrationWarning = "无法获取设备UDID，请确保手机已开启开发者模式并允许USB调试。";
                return;
            }

            // 检查云端注册状态
            var (cloudDevices, _) = await _ecoService.GetDeviceListAsync(pageSize: 100);
            var existing = cloudDevices.FirstOrDefault(d => d.Udid == udid);

            if (existing != null)
            {
                IsDeviceRegistered = true;
                DeviceRegistrationWarning = "";
            }
            else
            {
                IsDeviceRegistered = false;
                DeviceRegistrationWarning = "该设备尚未在云端注册，未注册的设备无法安装应用。请点击下方按钮注册设备。";
            }
        }
        catch (Exception ex)
        {
            DeviceRegistrationWarning = $"检查注册状态失败: {ex.Message}";
        }
        finally
        {
            IsCheckingRegistration = false;
        }
    }

    [RelayCommand]
    private async Task RegisterDeviceAsync()
    {
        if (string.IsNullOrEmpty(DeviceUdid) || !_ecoService.IsAuthenticated) return;

        try
        {
            IsRegisteringDevice = true;
            var name = $"zykit-{DeviceUdid[..Math.Min(10, DeviceUdid.Length)]}";
            var (success, failed) = await _ecoService.AddDevicesAsync(
                new List<CloudDeviceInfo> { new() { DeviceName = name, Udid = DeviceUdid, DeviceType = 1 } });

            if (failed == 0)
            {
                IsDeviceRegistered = true;
                DeviceRegistrationWarning = "";
            }
            else
            {
                DeviceRegistrationWarning = "注册失败，该设备可能已注册。如需帮助，请在左侧菜单「设备管理」中查看。";
            }
        }
        catch (Exception ex)
        {
            DeviceRegistrationWarning = $"注册失败: {ex.Message}\n\n请在左侧菜单「设备管理」中手动注册。";
        }
        finally
        {
            IsRegisteringDevice = false;
        }
    }

    [RelayCommand]
    private async Task ConnectWifiAsync()
    {
        if (string.IsNullOrEmpty(WifiIp3) || string.IsNullOrEmpty(WifiIp4))
        {
            DeviceErrorMessage = "请输入完整的IP地址";
            return;
        }
        try
        {
            IsConnectingWifi = true;
            DeviceErrorMessage = "";
            var (success, msg) = await _hdcService.ConnectDeviceAsync(FullWifiAddress);
            if (success)
            {
                DeviceStatus = "WiFi连接成功";
                await RefreshDevicesAsync();
            }
            else
            {
                DeviceErrorMessage = $"WiFi连接失败: {msg}\n\n请确认：\n1. 手机和电脑在同一网络\n2. IP地址和端口号正确\n3. 手机已开启无线调试";
            }
        }
        catch (Exception ex)
        {
            DeviceErrorMessage = $"WiFi连接失败: {ex.Message}";
        }
        finally
        {
            IsConnectingWifi = false;
        }
    }

    /// <summary>真机调试模式：从已连接的 USB 设备获取 UDID 填入手动输入框</summary>
    [RelayCommand]
    private async Task FetchUdidAsync()
    {
        try
        {
            IsFetchingUdid = true;
            ManualUdidHint = "正在获取设备 UDID...";
            var devices = await _hdcService.GetDeviceListVerboseAsync();
            if (devices.Count == 0)
            {
                ManualUdidHint = "未发现已连接设备，请手动输入 UDID（支持模拟器 UDID）";
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
                ManualUdidHint = "无法获取设备 UDID，请确保已开启开发者模式，或手动输入 UDID";
                return;
            }

            ManualUdid = udid;
            ManualUdidHint = $"已从设备 {device.DeviceId} 获取 UDID";
        }
        catch (Exception ex)
        {
            ManualUdidHint = $"获取 UDID 失败: {ex.Message}，可手动输入";
        }
        finally
        {
            IsFetchingUdid = false;
        }
    }

    #endregion

    #region 步骤3命令: 安装

    [RelayCommand]
    private async Task BrowseHapFileAsync()
    {
        // 由View层处理文件选择对话框
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

            // 真机调试：从 module.json 自动提取 ACL 权限（与可选 ACL 列表取交集）
            var extracted = EcoService.GetAclFromModuleJson(hapInfo.ModuleJson);
            if (extracted.Length > 0)
            {
                foreach (var item in AclPermissions)
                    item.IsSelected = extracted.Contains(item.Name);
            }
        }

        // 选择应用后自动加载签名资源并检查搭配
        _ = LoadSigningResourcesAsync();
    }

    [RelayCommand]
    private async Task LoadSigningResourcesAsync()
    {
        if (!_ecoService.IsAuthenticated) return;

        try
        {
            IsLoadingResources = true;
            PairingStatus = "";

            // 检查已有搭配关系
            SigningPair? existingPair = null;
            if (!string.IsNullOrEmpty(PackageName))
                existingPair = _pairingService.FindValidPair(PackageName);

            // 加载证书列表
            var localCertEntries = _cacheService.GetEntriesByType("cert");
            var cloudCerts = await _ecoService.GetCertListAsync();
            var allCerts = new List<UnifiedCertItem>();
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
                allCerts.Add(item);
            }
            foreach (var local in localByCloudId.Values)
            {
                allCerts.Add(new UnifiedCertItem
                {
                    Id = local.Id, Name = local.Name, TypeName = "本地证书",
                    StatusDisplay = local.StatusDisplay,
                    ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                    IsCloud = false, IsLocal = true, CloudId = local.CloudId,
                    LocalPath = local.LocalPath, LocalEntry = local,
                });
            }

            // 仅展示适用的证书（未过期的调试证书）
            AvailableCerts.Clear();
            var applicableCerts = allCerts.Where(c => c.StatusDisplay != "已过期").ToList();
            foreach (var c in applicableCerts)
                AvailableCerts.Add(c);
            HasApplicableCert = AvailableCerts.Count > 0;

            // 加载Profile列表
            var localProfileEntries = _cacheService.GetEntriesByType("profile");
            var (cloudProfiles, _) = await _ecoService.GetProfileListAsync(pageSize: 100);
            var allProfiles = new List<UnifiedProfileItem>();
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
                allProfiles.Add(item);
            }
            foreach (var local in localProfileByCloudId.Values)
            {
                allProfiles.Add(new UnifiedProfileItem
                {
                    Id = local.Id, Name = local.Name, TypeName = "本地Profile",
                    PackageName = local.PackageName, StatusDisplay = local.StatusDisplay,
                    ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                    IsLocal = true, LocalPath = local.LocalPath, LocalEntry = local,
                });
            }

            // 仅展示适用的Profile（未过期，且包名匹配或为空包名）
            AvailableProfiles.Clear();
            var applicableProfiles = allProfiles.Where(p =>
                p.StatusDisplay != "已过期" &&
                (string.IsNullOrEmpty(PackageName) ||
                 string.IsNullOrEmpty(p.PackageName) ||
                 p.PackageName == PackageName)).ToList();
            foreach (var p in applicableProfiles)
                AvailableProfiles.Add(p);
            HasApplicableProfile = AvailableProfiles.Count > 0;

            // 根据搭配关系填充默认值
            SelectedCert = null;
            SelectedProfile = null;

            if (existingPair != null)
            {
                PairingStatus = $"已找到该应用的签名搭配（证书: {existingPair.CertName}，Profile: {existingPair.ProfileName}），将自动使用";

                // 在可用列表中选中搭配对应的证书
                var matchedCert = AvailableCerts.FirstOrDefault(c => c.CloudId == existingPair.CertCloudId);
                if (matchedCert != null)
                    SelectedCert = matchedCert;

                // 在可用列表中选中搭配对应的Profile
                var matchedProfile = AvailableProfiles.FirstOrDefault(p => p.CloudId == existingPair.ProfileCloudId);
                if (matchedProfile != null)
                    SelectedProfile = matchedProfile;
            }
            else if (!string.IsNullOrEmpty(PackageName))
            {
                PairingStatus = HasApplicableCert && HasApplicableProfile
                    ? "未找到该应用的签名搭配，将从可用资源中自动选择或创建"
                    : "未找到该应用的签名搭配，且缺少适用的签名资源，请点击下方按钮快速生成";
            }
        }
        catch { }
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
            InstallErrorMessage = "请先选择要安装的HAP应用文件";
            return;
        }

        if (!_authService.IsLoggedIn)
        {
            InstallErrorMessage = "请先登录华为账号（返回第一步）";
            return;
        }

        // 真机调试模式：必须填写 UDID
        if (IsRealDeviceDebugMode && string.IsNullOrWhiteSpace(ManualUdid))
        {
            InstallErrorMessage = "真机调试模式下请先填写设备 UDID（返回第二步）";
            return;
        }

        try
        {
            IsInstalling = true;
            InstallCompleted = false;
            InstallSuccess = false;
            InstallErrorMessage = "";
            ProgressValue = 0;
            LogText = "";
            InstallStepIndex = 0;
            InstallStatus = "正在准备...";

            var deviceIp = SelectedDevice?.IsWifi == true ? SelectedDevice.DeviceId : null;

            // 真机调试模式：使用手动输入的 UDID + 选中的 ACL 权限
            string? debugUdid = null;
            List<string>? debugAcl = null;
            if (IsRealDeviceDebugMode)
            {
                debugUdid = ManualUdid;
                debugAcl = AclPermissions.Where(a => a.IsSelected).Select(a => a.Name).ToList();
                AppendLog($"真机调试模式：UDID={debugUdid}，ACL 权限 {debugAcl.Count} 项");
            }

            // 高级选项：使用手动选择的证书/Profile
            string? overrideCertId = null, overrideCertPath = null;
            string? overrideProfileId = null, overrideProfilePath = null;
            if (ShowAdvancedOptions && SelectedCert != null)
            {
                overrideCertId = SelectedCert.CloudId;
                overrideCertPath = SelectedCert.LocalPath;
            }
            if (ShowAdvancedOptions && SelectedProfile != null)
            {
                overrideProfileId = SelectedProfile.CloudId;
                overrideProfilePath = SelectedProfile.LocalPath;
            }

            // 步骤1-3: 验证账号并准备签名资源（真机调试模式传入 UDID + ACL）
            InstallStatus = "正在验证账号并准备签名资源...";
            var prepareSuccess = await _buildService.CheckAccountAndPrepareAsync(
                PackageName, null, deviceIp,
                overrideCertId, overrideCertPath, overrideProfileId, overrideProfilePath,
                debugUdid, debugAcl);
            if (!prepareSuccess)
            {
                InstallStatus = "准备失败";
                InstallErrorMessage = "签名资源准备失败。\n\n可能的原因：\n1. 账号登录已过期，请返回第一步重新登录\n2. 网络连接问题\n3. 设备未正确连接\n\n请查看下方日志了解详情，或在左侧菜单的「签名管理」和「Profile管理」中手动操作。";
                return;
            }

            ProgressValue = 60;

            // 步骤4-5: 签名并安装
            InstallStatus = "正在签名并安装应用...";
            var (success, message) = await _buildService.SignAndInstallAsync(HapFilePath, deviceIp);
            ProgressValue = 100;

            InstallCompleted = true;
            InstallSuccess = success;

            if (success)
            {
                InstallStatus = "安装完成";
                InstallResultMessage = "应用已成功安装到您的设备上！";
            }
            else
            {
                InstallStatus = "安装失败";
                InstallErrorMessage = $"安装失败: {message}\n\n可能的原因：\n1. 设备连接不稳定，请检查USB连接\n2. 应用签名异常\n3. 设备存储空间不足\n4. 设备未在云端注册\n\n请查看下方日志了解详情。";
            }
        }
        catch (Exception ex)
        {
            InstallStatus = "安装失败";
            InstallCompleted = true;
            InstallSuccess = false;
            InstallErrorMessage = $"安装过程中发生错误: {ex.Message}\n\n请查看下方日志了解详情，或在左侧菜单的各管理页面中手动操作。";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void ResetInstall()
    {
        InstallCompleted = false;
        InstallSuccess = false;
        InstallResultMessage = "";
        InstallErrorMessage = "";
        InstallStatus = "等待操作...";
        ProgressValue = 0;
        LogText = "";
        InstallStepIndex = -1;
    }

    /// <summary>快速生成签名资源（证书+Profile）</summary>
    [RelayCommand]
    private async Task QuickGenerateAsync()
    {
        if (string.IsNullOrEmpty(PackageName) || !_ecoService.IsAuthenticated) return;

        try
        {
            IsQuickGenerating = true;
            PairingStatus = "正在快速生成签名资源...";

            var deviceIp = SelectedDevice?.IsWifi == true ? SelectedDevice.DeviceId : null;

            // 真机调试模式：传入 UDID + ACL
            string? debugUdid = IsRealDeviceDebugMode ? ManualUdid : null;
            List<string>? debugAcl = IsRealDeviceDebugMode
                ? AclPermissions.Where(a => a.IsSelected).Select(a => a.Name).ToList()
                : null;

            var prepareSuccess = await _buildService.CheckAccountAndPrepareAsync(
                PackageName, null, deviceIp,
                udid: debugUdid, aclPermissions: debugAcl);

            if (prepareSuccess)
            {
                PairingStatus = "签名资源已生成，可以开始安装";
                // 重新加载资源列表
                await LoadSigningResourcesAsync();
            }
            else
            {
                PairingStatus = "签名资源生成失败，请查看日志或手动在左侧菜单中操作";
            }
        }
        catch (Exception ex)
        {
            PairingStatus = $"生成失败: {ex.Message}";
        }
        finally
        {
            IsQuickGenerating = false;
        }
    }

    /// <summary>全选 ACL 权限</summary>
    [RelayCommand]
    private void SelectAllAcl()
    {
        foreach (var item in AclPermissions) item.IsSelected = true;
    }

    /// <summary>清空 ACL 权限</summary>
    [RelayCommand]
    private void ClearAcl()
    {
        foreach (var item in AclPermissions) item.IsSelected = false;
    }

    #endregion

    #region 步骤导航

    [RelayCommand]
    private void NextStep()
    {
        if (CanGoNext && CurrentStepIndex < 2)
        {
            CurrentStepIndex++;
            // 进入设备步骤时自动检测设备（真机调试模式下也可用「从设备获取」填充 UDID）
            if (CurrentStepIndex == 1 && Devices.Count == 0)
                _ = RefreshDevicesAsync();
            // 进入安装步骤时自动加载签名资源
            if (CurrentStepIndex == 2 && AvailableCerts.Count == 0)
                _ = LoadSigningResourcesAsync();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CanGoBack)
            CurrentStepIndex--;
    }

    #endregion

    private void AppendLog(string text)
    {
        LogText += text + "\n";
    }
}
