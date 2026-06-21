using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class BuildService
{
    private readonly EcoService _eco;
    private readonly HdcService _hdc;
    private readonly SignService _sign;
    private readonly KeyStoreService _keyStore;
    private readonly LocalCacheService _cache;
    private readonly PairingService _pairing;
    private readonly ISdkPathProvider _sdk;

    public event Action<string, int>? StepStart;
    public event Action<string, string, string>? StepFinish;
    public event Action<string, string, string>? StepError;
    public event Action<string>? Log;

    private SigningConfig _config = new();

    public BuildService(EcoService eco, HdcService hdc, SignService sign,
        KeyStoreService keyStore, LocalCacheService cache, PairingService pairing, ISdkPathProvider sdk)
    {
        _eco = eco;
        _hdc = hdc;
        _sign = sign;
        _keyStore = keyStore;
        _cache = cache;
        _pairing = pairing;
        _sdk = sdk;
    }

    private void OnStepStart(string name, int index) => StepStart?.Invoke(name, index);
    private void OnStepFinish(string name, string value, string msg) => StepFinish?.Invoke(name, value, msg);
    private void OnStepError(string name, string error, string msg) => StepError?.Invoke(name, error, msg);
    private void OnLog(string msg) => Log?.Invoke(msg);

    public async Task<bool> CheckAccountAndPrepareAsync(string packageName, string? appId = null, string? deviceIp = null,
        string? overrideCertId = null, string? overrideCertPath = null,
        string? overrideProfileId = null, string? overrideProfilePath = null,
        string? udid = null, List<string>? aclPermissions = null)
    {
        // Step 1: 验证账号
        OnStepStart("账号验证", 0);
        try
        {
            await _eco.GetUserTeamListAsync();
            OnStepFinish("账号验证", "", "验证成功");
        }
        catch (Exception ex)
        {
            OnStepError("账号验证", ex.Message, "失败");
            return false;
        }

        // Step 2: 准备签名搭配 (缓存优先)
        OnStepStart("准备签名资源", 1);
        try
        {
            var pair = await PrepareSigningPairAsync(packageName, appId, deviceIp,
                overrideCertId, overrideCertPath, overrideProfileId, overrideProfilePath,
                udid, aclPermissions);
            _config.KeystorePath = pair.KeystorePath;
            _config.KeystorePassword = pair.KeystorePassword;
            _config.KeyAlias = pair.KeyAlias;
            _config.CertPath = pair.CertLocalPath;
            _config.CertId = pair.CertCloudId;
            _config.ProfilePath = pair.ProfileLocalPath;
            OnStepFinish("准备签名资源", pair.PackageName, pair.IsValid ? "搭配有效" : "已更新");
        }
        catch (Exception ex)
        {
            OnStepError("准备签名资源", ex.Message, "失败");
            return false;
        }

        return true;
    }

    public async Task<(bool success, string message)> SignAndInstallAsync(string hapPath, string? deviceIp = null)
    {
        OnStepStart("签名应用", 0);
        try
        {
            var outPath = _keyStore.GetSignedOutputPath(hapPath);
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            var (signSuccess, signMsg) = await _sign.SignHapAsync(
                _config.KeystorePath, _config.KeystorePassword, _config.KeyAlias,
                _config.CertPath, _config.ProfilePath, hapPath, outPath);

            if (!signSuccess) throw new Exception(signMsg);
            OnStepFinish("签名应用", "签名成功", "完成");
        }
        catch (Exception ex)
        {
            OnStepError("签名应用", ex.Message, "失败");
            return (false, ex.Message);
        }

        OnStepStart("安装应用", 1);
        try
        {
            var outPath = _keyStore.GetSignedOutputPath(hapPath);
            var (installSuccess, installMsg) = await _hdc.SendAndInstallAsync(outPath, deviceIp);
            if (!installSuccess) throw new Exception(installMsg);
            OnStepFinish("安装应用", "安装完成", "完成");

            // 更新搭配最后使用时间
            var pair = _pairing.FindPair(_config.KeystorePath.Contains("AUTO_ZYKIT") ? "" : "");
            // Best effort
        }
        catch (Exception ex)
        {
            OnStepError("安装应用", ex.Message, "失败");
            return (false, ex.Message);
        }

        return (true, "安装完成");
    }

    /// <summary>
    /// 准备签名搭配：优先使用已有有效搭配，否则创建新的
    /// </summary>
    private async Task<SigningPair> PrepareSigningPairAsync(string packageName, string? appId, string? deviceIp,
        string? overrideCertId = null, string? overrideCertPath = null,
        string? overrideProfileId = null, string? overrideProfilePath = null,
        string? udid = null, List<string>? aclPermissions = null)
    {
        // 1. 查找已有的有效搭配
        var existingPair = _pairing.FindValidPair(packageName, appId);
        if (existingPair != null
            && File.Exists(existingPair.KeystorePath)
            && File.Exists(existingPair.CertLocalPath)
            && File.Exists(existingPair.ProfileLocalPath))
        {
            OnLog($"使用已有签名搭配: {existingPair.CertName} + {existingPair.ProfileName}");
            _pairing.TouchPair(existingPair.Id);
            return existingPair;
        }

        // 2. 准备证书
        OnLog("准备证书...");
        string certId, certPath, certName;
        DateTime? certExpires;

        if (!string.IsNullOrEmpty(overrideCertId) && !string.IsNullOrEmpty(overrideCertPath) && File.Exists(overrideCertPath))
        {
            OnLog($"使用指定证书: {overrideCertId}");
            certId = overrideCertId;
            certPath = overrideCertPath;
            certName = Path.GetFileNameWithoutExtension(certPath);
            certExpires = null;
        }
        else
        {
            var (cid, cp, cn, ce) = await PrepareCertAsync(existingPair);
            certId = cid;
            certPath = cp;
            certName = cn;
            certExpires = ce;
        }

        // 3. 准备密钥库
        var ksName = existingPair?.KeystoreName ?? "AUTO_ZYKIT";
        var ksPath = _keyStore.GetKeystorePath(ksName);
        if (!File.Exists(ksPath))
        {
            var (ksSuccess, ksMsg) = await _keyStore.EnsureKeystoreAndCsrAsync(ksName);
            if (!ksSuccess) throw new Exception($"创建密钥库失败: {ksMsg}");
            _cache.CacheKeystore(ksName, ksPath, _keyStore.GetCsrPath(ksName));
        }

        // 4. 准备Profile
        OnLog("准备Profile...");
        string profileId, profilePath, profileName;
        DateTime? profileExpires;

        if (!string.IsNullOrEmpty(overrideProfileId) && !string.IsNullOrEmpty(overrideProfilePath) && File.Exists(overrideProfilePath))
        {
            OnLog($"使用指定Profile: {overrideProfileId}");
            profileId = overrideProfileId;
            profilePath = overrideProfilePath;
            profileName = Path.GetFileNameWithoutExtension(profilePath);
            profileExpires = null;
        }
        else
        {
            var (pid, pp, pn, pe) = await PrepareProfileAsync(packageName, deviceIp, certId, existingPair,
                udid, aclPermissions, appId);
            profileId = pid;
            profilePath = pp;
            profileName = pn;
            profileExpires = pe;
        }

        // 5. 保存搭配
        var pair = existingPair ?? new SigningPair { PackageName = packageName, AppId = appId ?? "" };
        pair.KeystoreName = ksName;
        pair.KeystorePath = ksPath;
        pair.KeystorePassword = "zykit123";
        pair.KeyAlias = "zykit";
        pair.CertCloudId = certId;
        pair.CertLocalPath = certPath;
        pair.CertName = certName;
        pair.CertExpiresAt = certExpires;
        pair.ProfileCloudId = profileId;
        pair.ProfileLocalPath = profilePath;
        pair.ProfileName = profileName;
        pair.ProfileExpiresAt = profileExpires;
        pair.LastUsedAt = DateTime.UtcNow;

        _pairing.SavePair(pair);
        OnLog($"签名搭配已保存: {pair.StatusDisplay}");
        return pair;
    }

    /// <summary>
    /// 真机调试：通过 UDID、ACL、包名生成调试 Profile（参考 hap_installer 的 autoCreateProfile）。
    /// 无需物理设备在线，支持模拟器 UDID。会自动注册设备、查询 AppId、创建带 ACL 的调试 Profile。
    /// </summary>
    /// <param name="udid">设备 UDID（可为模拟器 UDID）</param>
    /// <param name="packageName">应用包名</param>
    /// <param name="aclPermissions">ACL 权限列表</param>
    /// <param name="appId">可选 AppId，留空则按包名自动查询</param>
    /// <returns>(是否成功, Profile 本地路径, 描述信息)</returns>
    public async Task<(bool success, string profilePath, string message)> GenerateDebugProfileAsync(
        string udid, string packageName, List<string> aclPermissions, string? appId = null)
    {
        if (string.IsNullOrWhiteSpace(udid))
            return (false, "", "请输入设备 UDID");
        if (string.IsNullOrWhiteSpace(packageName))
            return (false, "", "请输入应用包名");

        // Step 1: 验证账号
        OnStepStart("账号验证", 0);
        try
        {
            await _eco.GetUserTeamListAsync();
            OnStepFinish("账号验证", "", "验证成功");
        }
        catch (Exception ex)
        {
            OnStepError("账号验证", ex.Message, "失败");
            return (false, "", $"账号验证失败: {ex.Message}");
        }

        // Step 2: 查询 AppId
        OnStepStart("查询 AppId", 1);
        string resolvedAppId = appId ?? "";
        try
        {
            if (string.IsNullOrEmpty(resolvedAppId))
            {
                OnLog($"按包名查询 AppId: {packageName}");
                var appIds = await _eco.GetAppIdByPackageNameAsync(packageName);
                if (appIds.Count > 0)
                {
                    resolvedAppId = appIds[0].AppId;
                    OnLog($"查询到 AppId: {resolvedAppId}");
                }
                else
                {
                    throw new Exception($"未查询到包名 {packageName} 对应的 AppId，请先在 AGC 创建应用");
                }
            }
            OnStepFinish("查询 AppId", resolvedAppId, "完成");
        }
        catch (Exception ex)
        {
            OnStepError("查询 AppId", ex.Message, "失败");
            return (false, "", $"查询 AppId 失败: {ex.Message}");
        }

        // Step 3: 注册设备（若未注册）
        OnStepStart("注册设备", 2);
        List<string> deviceIds;
        try
        {
            OnLog($"检查设备 UDID 是否已注册: {udid}");
            var (cloudDevices, _) = await _eco.GetDeviceListAsync(pageSize: 100);
            var existingDevice = cloudDevices.FirstOrDefault(d => d.Udid == udid);

            if (existingDevice == null)
            {
                var deviceName = $"zykit-{udid[..Math.Min(10, udid.Length)]}";
                OnLog($"注册设备: {deviceName}");
                await _eco.AddDevicesAsync(
                    new List<CloudDeviceInfo>
                    {
                        new() { DeviceName = deviceName, Udid = udid, DeviceType = 4 }
                    });
                (cloudDevices, _) = await _eco.GetDeviceListAsync(pageSize: 100);
                existingDevice = cloudDevices.FirstOrDefault(d => d.Udid == udid);
            }
            else
            {
                OnLog($"设备已注册: {existingDevice.DeviceName}");
            }

            if (existingDevice != null)
                deviceIds = new List<string> { existingDevice.Id };
            else
                deviceIds = cloudDevices.Select(d => d.Id).Where(id => !string.IsNullOrEmpty(id)).Take(100).ToList();

            OnStepFinish("注册设备", udid, $"绑定 {deviceIds.Count} 个设备");
        }
        catch (Exception ex)
        {
            OnStepError("注册设备", ex.Message, "失败");
            return (false, "", $"注册设备失败: {ex.Message}");
        }

        // Step 4: 准备证书
        OnStepStart("准备证书", 3);
        string certId, certPath;
        try
        {
            var (cid, cp, _, _) = await PrepareCertAsync(null);
            certId = cid;
            certPath = cp;
            OnLog($"使用证书: {certId}");
            OnStepFinish("准备证书", certId, "完成");
        }
        catch (Exception ex)
        {
            OnStepError("准备证书", ex.Message, "失败");
            return (false, "", $"准备证书失败: {ex.Message}");
        }

        // Step 5: 创建调试 Profile（带 ACL）
        OnStepStart("创建 Profile", 4);
        string profilePath;
        try
        {
            var provisionName = $"zykit-{packageName}";
            OnLog($"创建调试 Profile: {provisionName}");
            OnLog($"ACL 权限: {aclPermissions.Count} 项");

            var profile = await _eco.CreateProfileAsync(
                provisionName, 1, certId, resolvedAppId,
                deviceIds, aclPermissions);

            if (string.IsNullOrEmpty(profile.ProvisionDownloadUrl))
                throw new Exception("创建 Profile 失败: 未获取到下载链接");

            OnLog("下载 Profile");
            profilePath = _cache.GetProfileCachePath(profile.Id, packageName);
            await _eco.DownloadFileAsync(profile.ProvisionDownloadUrl, profilePath);
            if (!File.Exists(profilePath)) throw new Exception("Profile 文件下载失败");
            if (new FileInfo(profilePath).Length < 100) throw new Exception("Profile 文件下载不完整");

            _cache.CacheProfile(profile, profilePath, packageName);
            OnLog($"Profile 创建完成: {profilePath}");
            OnStepFinish("创建 Profile", profilePath, "完成");
        }
        catch (Exception ex)
        {
            OnStepError("创建 Profile", ex.Message, "失败");
            return (false, "", $"创建 Profile 失败: {ex.Message}");
        }

        return (true, profilePath, $"调试 Profile 生成成功: {profilePath}");
    }

    private async Task<(string id, string path, string name, DateTime? expires)> PrepareCertAsync(SigningPair? existingPair)
    {
        // 检查已有搭配的证书是否仍然有效
        if (existingPair != null && existingPair.IsCertValid && File.Exists(existingPair.CertLocalPath))
        {
            OnLog($"使用已有证书: {existingPair.CertName}");
            return (existingPair.CertCloudId, existingPair.CertLocalPath, existingPair.CertName, existingPair.CertExpiresAt);
        }

        // 查找本地缓存中未过期的证书
        var cachedCert = _cache.FindCertCacheByName("AUTO_ZYKIT");
        if (cachedCert != null && File.Exists(cachedCert.LocalPath))
        {
            OnLog($"使用缓存证书: {cachedCert.Name}");
            return (cachedCert.CloudId, cachedCert.LocalPath, cachedCert.Name, cachedCert.ExpiresAt > DateTime.MinValue ? cachedCert.ExpiresAt : null);
        }

        // 查找云端已有的未过期调试证书
        OnLog("查找云端证书...");
        var cloudCerts = await _eco.GetCertListAsync(certType: 1);
        var validCert = cloudCerts.FirstOrDefault(c => !c.IsExpired && !string.IsNullOrEmpty(c.CertDownloadUrl));
        if (validCert != null)
        {
            OnLog($"使用云端证书: {validCert.CertName}");
            var certPath = _cache.GetCertCachePath(validCert.Id);
            if (!File.Exists(certPath))
                await _eco.DownloadFileAsync(validCert.CertDownloadUrl, certPath);
            validCert.LocalPath = certPath;
            _cache.CacheCert(validCert, certPath);
            return (validCert.Id, certPath, validCert.CertName, validCert.ExpireTime > 0 ? validCert.ExpireTimeUtc : null);
        }

        // 创建新证书
        OnLog("创建新密钥库和证书...");
        var keystorePath = _keyStore.GetKeystorePath("AUTO_ZYKIT");
        var csrPath = _keyStore.GetCsrPath("AUTO_ZYKIT");

        foreach (var f in new[] { keystorePath, csrPath })
            if (File.Exists(f)) try { File.Delete(f); } catch { }

        var (ksSuccess, ksMsg) = await _keyStore.EnsureKeystoreAndCsrAsync("AUTO_ZYKIT");
        if (!ksSuccess) throw new Exception($"创建密钥库失败: {ksMsg}");
        _cache.CacheKeystore("AUTO_ZYKIT", keystorePath, csrPath);

        // 删除旧的同名证书
        var oldCerts = cloudCerts.Where(c => c.CertName == "AUTO_ZYKIT").ToList();
        if (oldCerts.Count > 0)
        {
            OnLog($"删除旧证书: {oldCerts.Count} 个");
            await _eco.DeleteCertsAsync(oldCerts.Select(c => c.Id).ToList());
        }

        var csrContent = _sign.ReadCsr(csrPath);
        var newCert = await _eco.CreateCertAsync("AUTO_ZYKIT", 1, csrContent);

        if (string.IsNullOrEmpty(newCert.CertDownloadUrl))
            throw new Exception("创建证书成功但无下载链接");

        var newCertPath = _cache.GetCertCachePath(newCert.Id);
        await _eco.DownloadFileAsync(newCert.CertDownloadUrl, newCertPath);
        newCert.LocalPath = newCertPath;
        _cache.CacheCert(newCert, newCertPath);

        OnLog($"证书创建完成: {newCertPath}");
        return (newCert.Id, newCertPath, newCert.CertName, newCert.ExpireTime > 0 ? newCert.ExpireTimeUtc : null);
    }

    private async Task<(string id, string path, string name, DateTime? expires)> PrepareProfileAsync(
        string packageName, string? deviceIp, string certId, SigningPair? existingPair,
        string? udid = null, List<string>? aclPermissions = null, string? appId = null)
    {
        // 检查已有搭配的Profile是否仍然有效
        if (existingPair != null && existingPair.IsProfileValid && File.Exists(existingPair.ProfileLocalPath))
        {
            OnLog($"使用已有Profile: {existingPair.ProfileName}");
            return (existingPair.ProfileCloudId, existingPair.ProfileLocalPath, existingPair.ProfileName, existingPair.ProfileExpiresAt);
        }

        // 查找本地缓存中未过期的Profile
        var cachedProfile = _cache.FindProfileCache(packageName);
        if (cachedProfile != null && File.Exists(cachedProfile.LocalPath))
        {
            OnLog($"使用缓存Profile: {cachedProfile.Name}");
            return (cachedProfile.CloudId, cachedProfile.LocalPath, cachedProfile.Name, cachedProfile.ExpiresAt > DateTime.MinValue ? cachedProfile.ExpiresAt : null);
        }

        // 创建新Profile
        OnLog("创建新Profile...");

        // 获取设备UDID：优先使用传入的 UDID（真机调试/模拟器），否则从已连接设备获取
        string udidValue;
        if (!string.IsNullOrEmpty(udid))
        {
            udidValue = udid;
            OnLog($"使用指定 UDID（真机调试）: {udidValue}");
        }
        else
        {
            string deviceKey;
            if (!string.IsNullOrEmpty(deviceIp))
            {
                deviceKey = deviceIp;
                OnLog($"连接设备: {deviceIp}");
                await _hdc.ConnectDeviceAsync(deviceIp);
            }
            else
            {
                var devices = await _hdc.GetDeviceListAsync();
                if (devices.Count == 0) throw new Exception("请连接手机，并开启开发者模式和USB调试；或在「真机调试」模式下手动输入 UDID");
                deviceKey = devices[0].DeviceId;
                OnLog($"使用设备: {deviceKey}");
            }

            OnLog("获取设备UDID");
            udidValue = await _hdc.GetUdidAsync(deviceKey);
            OnLog($"设备UDID: {udidValue}");
        }

        // 检查设备注册
        OnLog("检查设备注册状态");
        var (cloudDevices, _) = await _eco.GetDeviceListAsync(pageSize: 100);
        var existingDevice = cloudDevices.FirstOrDefault(d => d.Udid == udidValue);

        List<string> deviceIds;
        if (existingDevice == null)
        {
            OnLog($"注册设备: zykit-{udidValue[..Math.Min(10, udidValue.Length)]}");
            await _eco.AddDevicesAsync(
                new List<CloudDeviceInfo>
                {
                    new() { DeviceName = $"zykit-{udidValue[..Math.Min(10, udidValue.Length)]}", Udid = udidValue, DeviceType = 4 }
                });
            (cloudDevices, _) = await _eco.GetDeviceListAsync(pageSize: 100);
            existingDevice = cloudDevices.FirstOrDefault(d => d.Udid == udidValue);
        }

        if (existingDevice != null)
            deviceIds = new List<string> { existingDevice.Id };
        else
            deviceIds = cloudDevices.Select(d => d.Id).Where(id => !string.IsNullOrEmpty(id)).Take(100).ToList();

        // 解析 AppId：优先使用传入值，否则按包名查询
        string resolvedAppId = appId ?? "";
        if (string.IsNullOrEmpty(resolvedAppId))
        {
            OnLog($"按包名查询 AppId: {packageName}");
            var appIds = await _eco.GetAppIdByPackageNameAsync(packageName);
            if (appIds.Count > 0)
            {
                resolvedAppId = appIds[0].AppId;
                OnLog($"查询到 AppId: {resolvedAppId}");
            }
            else
            {
                OnLog("未查询到 AppId，将使用空值创建 Profile");
            }
        }

        var aclList = aclPermissions ?? new List<string>();
        OnLog($"使用证书ID: {certId}");
        OnLog($"包名: {packageName}");
        OnLog($"ACL 权限: {aclList.Count} 项");

        var profile = await _eco.CreateProfileAsync(
            $"zykit-{packageName}", 1, certId, resolvedAppId,
            deviceIds, aclList);

        if (string.IsNullOrEmpty(profile.ProvisionDownloadUrl))
            throw new Exception("创建Profile失败: 未获取到下载链接");

        OnLog("下载Profile");
        var finalPath = _cache.GetProfileCachePath(profile.Id, packageName);
        await _eco.DownloadFileAsync(profile.ProvisionDownloadUrl, finalPath);
        if (!File.Exists(finalPath)) throw new Exception("Profile文件下载失败");
        if (new FileInfo(finalPath).Length < 100) throw new Exception("Profile文件下载不完整");

        _cache.CacheProfile(profile, finalPath, packageName);
        OnLog($"Profile创建完成: {finalPath}");
        return (profile.Id, finalPath, profile.ProvisionName, profile.ExpireTime > 0 ? profile.ExpireTimeUtc : null);
    }
}
