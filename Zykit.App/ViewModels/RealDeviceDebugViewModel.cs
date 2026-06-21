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

/// <summary>ACL 权限勾选项（供 CheckBox 绑定）</summary>
public class AclPermissionItem : ObservableObject
{
    public string Name { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public partial class RealDeviceDebugViewModel : ViewModelBase
{
    private readonly BuildService _buildService;
    private readonly HdcService _hdcService;
    private readonly EcoService _ecoService;
    private readonly KeyStoreService _keyStoreService;
    private readonly AuthService _authService;

    // 输入参数
    [ObservableProperty] private string _udid = "";
    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private string _appId = "";
    [ObservableProperty] private string _hapFilePath = "";
    [ObservableProperty] private string _hapFileName = "未选择";

    // ACL 权限列表
    [ObservableProperty] private ObservableCollection<AclPermissionItem> _aclPermissions = new();

    // 运行状态
    [ObservableProperty] private bool _isGenerating = false;
    [ObservableProperty] private int _currentStep = -1;
    [ObservableProperty] private string _statusText = "等待操作...";
    [ObservableProperty] private int _progressValue = 0;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _generatedProfilePath = "";

    public string[] StepNames { get; } = { "账号验证", "查询 AppId", "注册设备", "准备证书", "创建 Profile" };

    public RealDeviceDebugViewModel(BuildService buildService, HdcService hdcService,
        EcoService ecoService, KeyStoreService keyStoreService, AuthService authService)
    {
        _buildService = buildService;
        _hdcService = hdcService;
        _ecoService = ecoService;
        _keyStoreService = keyStoreService;
        _authService = authService;

        // 初始化 ACL 权限列表
        foreach (var acl in EcoService.AclList)
            AclPermissions.Add(new AclPermissionItem { Name = acl, IsSelected = true });

        // 订阅构建服务事件
        _buildService.StepStart += (name, idx) =>
        {
            CurrentStep = idx;
            AppendLog($"[开始] {name}...");
        };
        _buildService.StepFinish += (name, value, msg) => AppendLog($"[完成] {name}: {msg}");
        _buildService.StepError += (name, error, msg) => AppendLog($"[错误] {name}: {error}");
        _buildService.Log += msg => AppendLog(msg);
    }

    /// <summary>从已连接的 USB 设备获取 UDID（也可手动输入模拟器 UDID）</summary>
    [RelayCommand]
    private async Task FetchUdidFromDeviceAsync()
    {
        try
        {
            var devices = await _hdcService.GetDeviceListVerboseAsync();
            if (devices.Count == 0)
            {
                StatusText = "未发现已连接设备，可手动输入 UDID（支持模拟器 UDID）";
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
                StatusText = "无法获取设备 UDID，请确保已开启开发者模式，或手动输入 UDID";
                return;
            }

            Udid = udid;
            StatusText = $"已从设备 {device.DeviceId} 获取 UDID";
        }
        catch (Exception ex)
        {
            StatusText = $"获取 UDID 失败: {ex.Message}，可手动输入";
        }
    }

    /// <summary>从 HAP 文件加载包名并自动提取 ACL 权限</summary>
    public void LoadFromHap(string filePath)
    {
        HapFilePath = filePath;
        HapFileName = System.IO.Path.GetFileName(filePath);

        var hapInfo = _keyStoreService.LoadHapInfo(filePath);
        if (hapInfo == null)
        {
            StatusText = "无法解析 HAP 文件";
            return;
        }

        PackageName = hapInfo.PackageName;

        // 从 module.json 自动提取 ACL 权限（与可选 ACL 列表取交集）
        var extracted = EcoService.GetAclFromModuleJson(hapInfo.ModuleJson);
        if (extracted.Length > 0)
        {
            foreach (var item in AclPermissions)
                item.IsSelected = extracted.Contains(item.Name);
            StatusText = $"已从 HAP 提取包名 {PackageName}，匹配 {extracted.Length} 项 ACL 权限";
        }
        else
        {
            StatusText = $"已从 HAP 提取包名 {PackageName}，未匹配到 ACL 权限（保留默认全选）";
        }
    }

    [RelayCommand]
    private void BrowseHapFile()
    {
        // 实际文件选择由 View 处理，调用 LoadFromHap
    }

    /// <summary>全选 ACL 权限</summary>
    [RelayCommand]
    private void SelectAllAcl()
    {
        foreach (var item in AclPermissions) item.IsSelected = true;
    }

    /// <summary>取消全选 ACL 权限</summary>
    [RelayCommand]
    private void ClearAcl()
    {
        foreach (var item in AclPermissions) item.IsSelected = false;
    }

    /// <summary>按包名查询 AppId</summary>
    [RelayCommand]
    private async Task QueryAppIdAsync()
    {
        if (!_ecoService.IsAuthenticated) { StatusText = "请先登录华为账号"; return; }
        if (string.IsNullOrEmpty(PackageName)) { StatusText = "请先输入包名"; return; }
        try
        {
            var result = await _ecoService.GetAppIdByPackageNameAsync(PackageName);
            if (result.Count > 0)
            {
                AppId = result[0].AppId;
                StatusText = $"查询到 AppId: {AppId}";
            }
            else
            {
                StatusText = "未查询到 AppId，请确认包名或前往 AGC 创建应用";
            }
        }
        catch (Exception ex) { StatusText = $"查询 AppId 失败: {ex.Message}"; }
    }

    /// <summary>通过 UDID、ACL、包名生成调试 Profile</summary>
    [RelayCommand]
    private async Task GenerateDebugProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(Udid)) { StatusText = "请输入设备 UDID（支持模拟器 UDID）"; return; }
        if (string.IsNullOrWhiteSpace(PackageName)) { StatusText = "请输入应用包名"; return; }
        if (!_authService.IsLoggedIn) { StatusText = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）"; return; }

        try
        {
            IsGenerating = true;
            ProgressValue = 0;
            LogText = "";
            CurrentStep = 0;
            GeneratedProfilePath = "";
            StatusText = "正在生成调试 Profile...";

            var acl = AclPermissions.Where(a => a.IsSelected).Select(a => a.Name).ToList();
            AppendLog($"UDID: {Udid}");
            AppendLog($"包名: {PackageName}");
            AppendLog($"AppId: {(string.IsNullOrEmpty(AppId) ? "自动查询" : AppId)}");
            AppendLog($"ACL 权限: {acl.Count} 项");

            var (success, profilePath, message) = await _buildService.GenerateDebugProfileAsync(
                Udid, PackageName, acl, string.IsNullOrEmpty(AppId) ? null : AppId);

            ProgressValue = 100;

            if (success)
            {
                GeneratedProfilePath = profilePath;
                StatusText = $"生成成功: {profilePath}";
                AppendLog(message);
            }
            else
            {
                StatusText = $"生成失败: {message}";
                AppendLog($"[失败] {message}");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"生成失败: {ex.Message}";
            AppendLog($"[异常] {ex.Message}");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private void AppendLog(string text)
    {
        LogText += text + "\n";
    }
}
