using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

/// <summary>统一证书显示项</summary>
public class UnifiedCertItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
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
    public CloudCertInfo? CloudCert { get; set; }
    public LocalCacheEntry? LocalEntry { get; set; }
}

public partial class SigningViewModel : ViewModelBase
{
    private readonly SignService _signService;
    private readonly KeyStoreService _keyStoreService;
    private readonly EcoService _ecoService;
    private readonly LocalCacheService _cacheService;
    private readonly PairingService _pairingService;
    private readonly ISdkPathProvider _sdk;
    private readonly AuthService _authService;

    // 密钥库配置
    [ObservableProperty] private string _keystoreName = "AUTO_ZYKIT";
    [ObservableProperty] private string _keystorePassword = "zykit123";
    [ObservableProperty] private string _keyAlias = "zykit";
    [ObservableProperty] private string _keystoreStatus = "未创建";
    [ObservableProperty] private string _csrStatus = "未创建";
    [ObservableProperty] private bool _isCreatingKeystore = false;
    [ObservableProperty] private bool _isCreatingCsr = false;

    // 统一证书列表
    [ObservableProperty] private ObservableCollection<UnifiedCertItem> _unifiedCerts = new();
    [ObservableProperty] private UnifiedCertItem? _selectedCert;
    [ObservableProperty] private ObservableCollection<UnifiedCertItem> _selectedCerts = new();
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _operationStatus = "";

    // 创建证书表单参数
    [ObservableProperty] private string _newCertName = "AUTO_ZYKIT";
    [ObservableProperty] private int _newCertType = 1; // 1=调试
    [ObservableProperty] private string _newCertKeystoreName = "AUTO_ZYKIT";
    [ObservableProperty] private string _newCertKeystorePassword = "zykit123";
    [ObservableProperty] private string _newCertKeyAlias = "zykit";
    [ObservableProperty] private bool _isCreatingCert = false;
    [ObservableProperty] private string _certFormHint = "证书名称将同时作为密钥库别名，建议使用默认值";

    // 签名搭配
    [ObservableProperty] private ObservableCollection<SigningPair> _signingPairs = new();
    [ObservableProperty] private SigningPair? _selectedPair;

    // UI状态
    [ObservableProperty] private bool _showCreateCertForm = false;
    [ObservableProperty] private bool _showKeystoreSection = false;
    [ObservableProperty] private bool _showKeystorePassword = false;
    [ObservableProperty] private bool _showCertKeystorePassword = false;

    public SigningViewModel(SignService signService, KeyStoreService keyStoreService,
        EcoService ecoService, LocalCacheService cacheService, PairingService pairingService, ISdkPathProvider sdk,
        AuthService authService)
    {
        _signService = signService;
        _keyStoreService = keyStoreService;
        _ecoService = ecoService;
        _cacheService = cacheService;
        _pairingService = pairingService;
        _sdk = sdk;
        _authService = authService;

        _authService.LoginSuccess += OnLoginSuccess;
        CheckExistingFiles();
        RefreshSigningPairs();
        RefreshUnifiedList();
    }

    private void CheckExistingFiles()
    {
        var ksPath = _keyStoreService.GetKeystorePath(KeystoreName);
        KeystoreStatus = File.Exists(ksPath) ? $"已存在" : "未创建";
        var csrPath = _keyStoreService.GetCsrPath(KeystoreName);
        CsrStatus = File.Exists(csrPath) ? $"已存在" : "未创建";
    }

    private void RefreshSigningPairs()
    {
        SigningPairs.Clear();
        foreach (var p in _pairingService.GetAllPairs())
            SigningPairs.Add(p);
    }

    [RelayCommand]
    private async Task RefreshUnifiedListAsync()
    {
        try
        {
            IsLoading = true;
            var localEntries = _cacheService.GetEntriesByType("cert");
            var cloudCerts = _ecoService.IsAuthenticated
                ? await _ecoService.GetCertListAsync()
                : new List<CloudCertInfo>();

            UnifiedCerts.Clear();
            var localByCloudId = localEntries.Where(e => !string.IsNullOrEmpty(e.CloudId))
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
                UnifiedCerts.Add(item);
            }

            foreach (var local in localByCloudId.Values)
            {
                UnifiedCerts.Add(new UnifiedCertItem
                {
                    Id = local.Id, Name = local.Name, TypeName = "本地证书",
                    StatusDisplay = local.StatusDisplay,
                    ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                    IsCloud = false, IsLocal = true, CloudId = local.CloudId,
                    LocalPath = local.LocalPath, LocalEntry = local,
                });
            }

            OperationStatus = $"共 {UnifiedCerts.Count} 个证书";
        }
        catch (Exception ex) { OperationStatus = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private void RefreshUnifiedList()
    {
        var localEntries = _cacheService.GetEntriesByType("cert");
        UnifiedCerts.Clear();
        foreach (var local in localEntries)
        {
            UnifiedCerts.Add(new UnifiedCertItem
            {
                Id = local.Id, Name = local.Name, TypeName = "本地证书",
                StatusDisplay = local.StatusDisplay,
                ExpiresAt = local.ExpiresAt > DateTime.MinValue ? local.ExpiresAt : null,
                IsCloud = false, IsLocal = true, CloudId = local.CloudId,
                LocalPath = local.LocalPath, LocalEntry = local,
            });
        }
    }

    #region 密钥库操作

    [RelayCommand]
    private async Task CreateKeystoreAsync()
    {
        try
        {
            IsCreatingKeystore = true;
            var ksPath = _keyStoreService.GetKeystorePath(KeystoreName);
            var (success, msg) = await _signService.CreateKeystoreAsync(ksPath, KeystorePassword, KeyAlias, KeystoreName);
            KeystoreStatus = success ? "创建成功" : $"失败: {msg}";
            if (success) _cacheService.CacheKeystore(KeystoreName, ksPath, _keyStoreService.GetCsrPath(KeystoreName));
        }
        catch (Exception ex) { KeystoreStatus = $"失败: {ex.Message}"; }
        finally { IsCreatingKeystore = false; CheckExistingFiles(); }
    }

    [RelayCommand]
    private async Task CreateCsrAsync()
    {
        try
        {
            IsCreatingCsr = true;
            var ksPath = _keyStoreService.GetKeystorePath(KeystoreName);
            var csrPath = _keyStoreService.GetCsrPath(KeystoreName);
            var (success, msg) = await _signService.CreateCsrAsync(ksPath, csrPath, KeyAlias, KeystorePassword);
            CsrStatus = success ? "创建成功" : $"失败: {msg}";
        }
        catch (Exception ex) { CsrStatus = $"失败: {ex.Message}"; }
        finally { IsCreatingCsr = false; CheckExistingFiles(); }
    }

    #endregion

    #region 创建证书（带参数表单）

    /// <summary>自动填充证书创建表单默认值</summary>
    [RelayCommand]
    private void AutoFillCertForm()
    {
        NewCertName = "AUTO_ZYKIT";
        NewCertKeystoreName = "AUTO_ZYKIT";
        NewCertKeystorePassword = "zykit123";
        NewCertKeyAlias = "zykit";
        NewCertType = 1;
        CertFormHint = "已填入推荐默认值，直接点击创建即可";
    }

    /// <summary>检查密钥库是否已存在，如果存在则提示</summary>
    [RelayCommand]
    private async Task CheckKeystoreExistsAsync()
    {
        var ksPath = _keyStoreService.GetKeystorePath(NewCertKeystoreName);
        var csrPath = _keyStoreService.GetCsrPath(NewCertKeystoreName);
        if (File.Exists(ksPath) && File.Exists(csrPath))
        {
            CertFormHint = "密钥库和CSR已存在，将直接使用现有文件申请证书";
        }
        else if (File.Exists(ksPath))
        {
            CertFormHint = "密钥库已存在但缺少CSR，将自动补充生成CSR";
        }
        else
        {
            CertFormHint = "密钥库不存在，将自动创建密钥库和CSR后申请证书";
        }
    }

    [RelayCommand]
    private async Task CreateCloudCertAsync()
    {
        if (!_ecoService.IsAuthenticated) { OperationStatus = "请先登录华为账号（可在左侧「账号管理」或「快速开始」中登录）"; return; }
        if (string.IsNullOrEmpty(NewCertName)) { OperationStatus = "请输入证书名称"; return; }

        try
        {
            IsCreatingCert = true;
            var ksName = string.IsNullOrEmpty(NewCertKeystoreName) ? NewCertName : NewCertKeystoreName;
            var ksPass = string.IsNullOrEmpty(NewCertKeystorePassword) ? "zykit123" : NewCertKeystorePassword;
            var ksAlias = string.IsNullOrEmpty(NewCertKeyAlias) ? ksName : NewCertKeyAlias;

            // Step 1: 确保密钥库和CSR存在
            OperationStatus = "准备密钥库和CSR...";
            var ksPath = _keyStoreService.GetKeystorePath(ksName);
            var csrPath = _keyStoreService.GetCsrPath(ksName);

            if (!File.Exists(ksPath) || !File.Exists(csrPath))
            {
                // 清理旧文件重建
                if (!File.Exists(ksPath))
                {
                    var (ksSuccess, ksMsg) = await _signService.CreateKeystoreAsync(ksPath, ksPass, ksAlias, ksName);
                    if (!ksSuccess) { OperationStatus = $"创建密钥库失败: {ksMsg}"; return; }
                }
                if (!File.Exists(csrPath))
                {
                    var (csrSuccess, csrMsg) = await _signService.CreateCsrAsync(ksPath, csrPath, ksAlias, ksPass);
                    if (!csrSuccess) { OperationStatus = $"生成CSR失败: {csrMsg}"; return; }
                }
                _cacheService.CacheKeystore(ksName, ksPath, csrPath);
            }

            // Step 2: 申请云端证书
            OperationStatus = "向华为AGC申请证书...";
            var csrContent = _signService.ReadCsr(csrPath);
            var cert = await _ecoService.CreateCertAsync(NewCertName, NewCertType, csrContent);

            // Step 3: 下载证书到本地
            if (!string.IsNullOrEmpty(cert.CertDownloadUrl))
            {
                OperationStatus = "下载证书文件...";
                var certPath = _cacheService.GetCertCachePath(cert.Id);
                await _ecoService.DownloadFileAsync(cert.CertDownloadUrl, certPath);
                cert.LocalPath = certPath;
                _cacheService.CacheCert(cert, certPath);
            }

            OperationStatus = $"证书创建成功: {cert.CertName} ({cert.CertTypeName})";
            CertFormHint = "证书已创建并下载到本地，可用于签名";
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"创建失败: {ex.Message}";
            CertFormHint = $"创建失败，请检查参数后重试。错误: {ex.Message}";
        }
        finally { IsCreatingCert = false; }
    }

    #endregion

    #region 证书操作

    [RelayCommand]
    private async Task DownloadCertAsync()
    {
        if (SelectedCert == null || string.IsNullOrEmpty(SelectedCert.DownloadUrl)) return;
        try
        {
            var certPath = _cacheService.GetCertCachePath(SelectedCert.CloudId);
            await _ecoService.DownloadFileAsync(SelectedCert.DownloadUrl, certPath);
            if (SelectedCert.CloudCert != null)
                _cacheService.CacheCert(SelectedCert.CloudCert, certPath);
            await RefreshUnifiedListAsync();
            OperationStatus = "证书已下载到本地";
        }
        catch (Exception ex) { OperationStatus = $"下载失败: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteCertAsync()
    {
        if (SelectedCert == null) return;
        try
        {
            if (SelectedCert.IsCloud && _ecoService.IsAuthenticated)
                await _ecoService.DeleteCertsAsync(new List<string> { SelectedCert.CloudId });
            if (SelectedCert.IsLocal && SelectedCert.LocalEntry != null)
            {
                if (File.Exists(SelectedCert.LocalPath)) try { File.Delete(SelectedCert.LocalPath); } catch { }
                _cacheService.RemoveEntry(SelectedCert.LocalEntry.Id);
            }
            OperationStatus = $"已删除: {SelectedCert.Name}";
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex) { OperationStatus = $"删除失败: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteSelectedCertsAsync()
    {
        if (SelectedCerts.Count == 0) return;
        try
        {
            var toDelete = SelectedCerts.ToList();
            var cloudIds = toDelete.Where(c => c.IsCloud && !string.IsNullOrEmpty(c.CloudId)).Select(c => c.CloudId).ToList();
            if (cloudIds.Count > 0 && _ecoService.IsAuthenticated)
                await _ecoService.DeleteCertsAsync(cloudIds);
            foreach (var cert in toDelete.Where(c => c.IsLocal && c.LocalEntry != null))
            {
                if (File.Exists(cert.LocalPath)) try { File.Delete(cert.LocalPath); } catch { }
                _cacheService.RemoveEntry(cert.LocalEntry!.Id);
            }
            OperationStatus = $"已删除 {toDelete.Count} 个证书";
            SelectedCerts.Clear();
            await RefreshUnifiedListAsync();
        }
        catch (Exception ex) { OperationStatus = $"批量删除失败: {ex.Message}"; }
    }

    [RelayCommand]
    private void ClearExpired()
    {
        _cacheService.ClearExpired();
        RefreshSigningPairs();
        _ = RefreshUnifiedListAsync();
        OperationStatus = "已清理过期缓存";
    }

    #endregion

    #region 签名搭配管理

    [RelayCommand]
    private void DeletePair()
    {
        if (SelectedPair == null) return;
        _pairingService.DeletePair(SelectedPair.Id);
        RefreshSigningPairs();
    }

    [RelayCommand]
    private void ClearExpiredPairs()
    {
        _pairingService.ClearExpiredPairs();
        RefreshSigningPairs();
        OperationStatus = "已清理过期搭配";
    }

    #endregion

    private void OnLoginSuccess(AuthInfo auth)
    {
        _ = RefreshUnifiedListAsync();
    }
}
