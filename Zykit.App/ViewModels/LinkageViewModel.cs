using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Enums;
using SukiUI.Toasts;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class LinkageViewModel : ViewModelBase
{
    private readonly HokitProtocolService _hokitProtocol;
    private readonly AuthService _authService;
    private readonly HdcService _hdcService;
    private readonly BuildService _buildService;
    private readonly KeyStoreService _keyStoreService;
    private readonly EcoService _ecoService;
    private readonly LocalCacheService _cacheService;
    private readonly PairingService _pairingService;
    private readonly ISukiToastManager _toast;

    #region 步骤跟踪

    [ObservableProperty]
    private int _currentStepIndex = 0;

    public IEnumerable<string> Steps { get; } = new[] { "确认下载", "连接设备", "安装应用" };

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

    partial void OnDownloadCompletedChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnIsDeviceReadyChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnManualUdidChanged(string value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnIsRealDeviceDebugModeChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(CanGoBack));

    public bool CanGoNext => CurrentStepIndex switch
    {
        0 => DownloadCompleted,
        1 => IsRealDeviceDebugMode
            ? !string.IsNullOrWhiteSpace(ManualUdid)
            : IsDeviceReady,
        _ => false
    };

    public bool CanGoBack => CurrentStepIndex > 0 && !IsInstalling;

    #endregion

    /// <summary>
    /// 请求关闭联动操作面板（由 MainWindow 订阅，从侧边栏移除该菜单项）。
    /// </summary>
    public event Action? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    #region 步骤1: 确认下载

    /// <summary>是否有待处理的协议数据</summary>
    [ObservableProperty]
    private bool _hasPendingData = false;

    /// <summary>无数据时的提示信息</summary>
    [ObservableProperty]
    private string _noDataMessage = "暂无联动操作。请在浏览器中点击 hokit:// 链接以唤起本页面。";

    /// <summary>解码后的下载地址</summary>
    [ObservableProperty]
    private string _downloadUrl = "";

    /// <summary>请求头显示文本</summary>
    [ObservableProperty]
    private string _headersDisplay = "";

    /// <summary>来源标识</summary>
    [ObservableProperty]
    private string _source = "";

    /// <summary>下载的文件路径</summary>
    [ObservableProperty]
    private string _downloadedFilePath = "";

    /// <summary>下载的文件名</summary>
    [ObservableProperty]
    private string _downloadedFileName = "未下载";

    /// <summary>是否正在下载</summary>
    [ObservableProperty]
    private bool _isDownloading = false;

    /// <summary>下载状态</summary>
    [ObservableProperty]
    private string _downloadStatus = "等待下载";

    /// <summary>下载进度</summary>
    [ObservableProperty]
    private int _downloadProgress = 0;

    /// <summary>下载错误信息</summary>
    [ObservableProperty]
    private string _downloadErrorMessage = "";

    /// <summary>下载是否完成</summary>
    [ObservableProperty]
    private bool _downloadCompleted = false;

    /// <summary>下载完成后解析出的包名</summary>
    [ObservableProperty]
    private string _packageName = "";

    /// <summary>下载完成后解析出的应用名</summary>
    [ObservableProperty]
    private string _appName = "";

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

    public string FullWifiAddress => $"{WifiIp1}.{WifiIp2}.{WifiIp3}.{WifiIp4}:{WifiPort}";

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

    [ObservableProperty]
    private string _pairingStatus = "";

    [ObservableProperty]
    private bool _hasApplicableCert = true;

    [ObservableProperty]
    private bool _hasApplicableProfile = true;

    [ObservableProperty]
    private bool _isQuickGenerating = false;

    [ObservableProperty]
    private ObservableCollection<AclPermissionItem> _aclPermissions = new();

    [ObservableProperty]
    private bool _showAclOptions = false;

    #endregion

    public LinkageViewModel(HokitProtocolService hokitProtocol, AuthService authService,
        HdcService hdcService, BuildService buildService, KeyStoreService keyStoreService,
        EcoService ecoService, LocalCacheService cacheService, PairingService pairingService,
        ISukiToastManager toast)
    {
        _hokitProtocol = hokitProtocol;
        _authService = authService;
        _hdcService = hdcService;
        _buildService = buildService;
        _keyStoreService = keyStoreService;
        _ecoService = ecoService;
        _cacheService = cacheService;
        _pairingService = pairingService;
        _toast = toast;

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

        // 加载待处理的协议数据
        LoadPendingData();
    }

    /// <summary>
    /// 加载待处理的协议数据。当通过 hokit:// 协议唤起时（无论是首次启动还是已运行实例
    /// 收到管道消息），都会调用此方法刷新下载信息并重置向导状态。
    /// </summary>
    public void LoadPendingData()
    {
        var data = _hokitProtocol.PendingData;
        if (data == null)
        {
            HasPendingData = false;
            return;
        }

        HasPendingData = true;
        DownloadUrl = data.Url;
        HeadersDisplay = data.Headers.Count > 0
            ? string.Join("\n", data.Headers)
            : "（无）";
        Source = string.IsNullOrEmpty(data.Source) ? "（无）" : data.Source;

        // 重置向导状态，确保新的协议唤起从头开始
        IsDownloading = false;
        DownloadProgress = 0;
        DownloadStatus = "等待下载";
        DownloadCompleted = false;
        DownloadErrorMessage = "";
        DownloadedFileName = "未下载";
        DownloadedFilePath = "";
        PackageName = "";

        // 回到第一步
        CurrentStepIndex = 0;
    }

    #region 步骤1命令: 下载

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_hokitProtocol.PendingData == null)
        {
            DownloadErrorMessage = "无待处理的协议数据";
            return;
        }

        try
        {
            IsDownloading = true;
            DownloadCompleted = false;
            DownloadErrorMessage = "";
            DownloadProgress = 0;
            DownloadStatus = "正在下载...";

            var progress = new Progress<(long received, long? total)>(p =>
            {
                if (p.total.HasValue && p.total.Value > 0)
                {
                    DownloadProgress = (int)(p.received * 100 / p.total.Value);
                    DownloadStatus = $"正在下载... {DownloadProgress}% ({FormatBytes(p.received)}/{FormatBytes(p.total.Value)})";
                }
                else
                {
                    DownloadStatus = $"正在下载... {FormatBytes(p.received)}";
                }
            });

            var filePath = await _hokitProtocol.DownloadAsync(_hokitProtocol.PendingData, progress);

            DownloadedFilePath = filePath;
            DownloadedFileName = Path.GetFileName(filePath);
            DownloadProgress = 100;
            DownloadStatus = "下载完成";
            DownloadCompleted = true;

            // 解析 HAP 信息
            var hapInfo = _keyStoreService.LoadHapInfo(filePath);
            if (hapInfo != null)
            {
                PackageName = hapInfo.PackageName;
                AppName = hapInfo.AppName;

                // 自动提取 ACL 权限
                var extracted = EcoService.GetAclFromModuleJson(hapInfo.ModuleJson);
                if (extracted.Length > 0)
                {
                    foreach (var item in AclPermissions)
                        item.IsSelected = extracted.Contains(item.Name);
                }
            }

            _toast.CreateToast()
                .WithTitle("下载完成")
                .WithContent($"文件已下载: {DownloadedFileName}")
                .OfType(NotificationType.Success)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            DownloadStatus = "下载失败";
            DownloadErrorMessage = $"下载失败: {ex.Message}";
            _toast.CreateToast()
                .WithTitle("下载失败")
                .WithContent(ex.Message)
                .OfType(NotificationType.Error)
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>导航到下一步</summary>
    [RelayCommand]
    private void NextStep()
    {
        if (CanGoNext && CurrentStepIndex < 2)
        {
            CurrentStepIndex++;
            // 进入设备步骤时自动检测设备
            if (CurrentStepIndex == 1 && Devices.Count == 0)
                _ = RefreshDevicesAsync();
            // 进入安装步骤时自动加载签名资源
            if (CurrentStepIndex == 2 && AvailableCerts.Count == 0)
                _ = LoadSigningResourcesAsync();
        }
    }

    /// <summary>导航到上一步</summary>
    [RelayCommand]
    private void PreviousStep()
    {
        if (CanGoBack)
            CurrentStepIndex--;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
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
                _ = CheckDeviceRegistrationAsync();
            }
            else
            {
                IsDeviceReady = false;
                DeviceStatus = "未检测到设备";
                DeviceErrorMessage = "未检测到已连接的设备，请确认：\n\n1. USB线已连接到电脑\n2. 手机已开启开发者模式\n3. 手机上已允许USB调试\n\n如果使用WiFi连接，请点击下方「无线连接」展开高级选项。";
            }
        }
        catch (Exception ex)
        {
            IsDeviceReady = false;
            DeviceStatus = "检测失败";
            DeviceErrorMessage = $"设备检测失败: {ex.Message}\n\n请确认hdc工具可用，可在左侧菜单「设置」中检查SDK路径。";
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
    private async Task LoadSigningResourcesAsync()
    {
        if (!_ecoService.IsAuthenticated) return;

        try
        {
            IsLoadingResources = true;
            PairingStatus = "";

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

            AvailableProfiles.Clear();
            var applicableProfiles = allProfiles.Where(p =>
                p.StatusDisplay != "已过期" &&
                (string.IsNullOrEmpty(PackageName) ||
                 string.IsNullOrEmpty(p.PackageName) ||
                 p.PackageName == PackageName)).ToList();
            foreach (var p in applicableProfiles)
                AvailableProfiles.Add(p);
            HasApplicableProfile = AvailableProfiles.Count > 0;

            SelectedCert = null;
            SelectedProfile = null;

            if (existingPair != null)
            {
                PairingStatus = $"已找到该应用的签名搭配（证书: {existingPair.CertName}，Profile: {existingPair.ProfileName}），将自动使用";

                var matchedCert = AvailableCerts.FirstOrDefault(c => c.CloudId == existingPair.CertCloudId);
                if (matchedCert != null)
                    SelectedCert = matchedCert;

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
        if (string.IsNullOrEmpty(DownloadedFilePath))
        {
            InstallErrorMessage = "请先返回第一步下载应用文件";
            return;
        }

        if (!_authService.IsLoggedIn)
        {
            InstallErrorMessage = "请先登录华为账号（在左侧菜单「账号管理」中登录）";
            return;
        }

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

            string? debugUdid = null;
            List<string>? debugAcl = null;
            if (IsRealDeviceDebugMode)
            {
                debugUdid = ManualUdid;
                debugAcl = AclPermissions.Where(a => a.IsSelected).Select(a => a.Name).ToList();
                AppendLog($"真机调试模式：UDID={debugUdid}，ACL 权限 {debugAcl.Count} 项");
            }

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

            InstallStatus = "正在验证账号并准备签名资源...";
            var prepareSuccess = await _buildService.CheckAccountAndPrepareAsync(
                PackageName, null, deviceIp,
                overrideCertId, overrideCertPath, overrideProfileId, overrideProfilePath,
                debugUdid, debugAcl);
            if (!prepareSuccess)
            {
                InstallStatus = "准备失败";
                InstallErrorMessage = "签名资源准备失败。\n\n可能的原因：\n1. 账号登录已过期，请在左侧菜单「账号管理」中重新登录\n2. 网络连接问题\n3. 设备未正确连接\n\n请查看下方日志了解详情。";
                return;
            }

            ProgressValue = 60;

            InstallStatus = "正在签名并安装应用...";
            var (success, message) = await _buildService.SignAndInstallAsync(DownloadedFilePath, deviceIp);
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
            InstallErrorMessage = $"安装过程中发生错误: {ex.Message}\n\n请查看下方日志了解详情。";
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

    [RelayCommand]
    private async Task QuickGenerateAsync()
    {
        if (string.IsNullOrEmpty(PackageName) || !_ecoService.IsAuthenticated) return;

        try
        {
            IsQuickGenerating = true;
            PairingStatus = "正在快速生成签名资源...";

            var deviceIp = SelectedDevice?.IsWifi == true ? SelectedDevice.DeviceId : null;

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

    [RelayCommand]
    private void SelectAllAcl()
    {
        foreach (var item in AclPermissions) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAcl()
    {
        foreach (var item in AclPermissions) item.IsSelected = false;
    }

    #endregion

    private void AppendLog(string text)
    {
        LogText += text + "\n";
    }
}
