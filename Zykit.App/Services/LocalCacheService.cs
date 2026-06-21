using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Zykit.App.Json;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class LocalCacheService
{
    private readonly ISdkPathProvider _sdk;
    private readonly string _cacheDir;
    private readonly string _indexFile;
    private List<LocalCacheEntry> _entries = new();

    public LocalCacheService(ISdkPathProvider sdk)
    {
        _sdk = sdk;
        _cacheDir = Path.Combine(_sdk.ConfigDir, "cache");
        _indexFile = Path.Combine(_cacheDir, "index.json");
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(Path.Combine(_cacheDir, "certs"));
        Directory.CreateDirectory(Path.Combine(_cacheDir, "profiles"));
        Directory.CreateDirectory(Path.Combine(_cacheDir, "keystores"));
        LoadIndex();
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexFile)) return;
        try
        {
            var json = File.ReadAllText(_indexFile);
            _entries = JsonSerializer.Deserialize(json, ZykitJsonContext.Default.ListLocalCacheEntry) ?? new();
        }
        catch { _entries = new(); }
    }

    private void SaveIndex()
    {
        var json = JsonSerializer.Serialize(_entries, ZykitJsonContext.Default.ListLocalCacheEntry);
        File.WriteAllText(_indexFile, json);
    }

    #region 证书缓存

    public string GetCertCachePath(string certId) => Path.Combine(_cacheDir, "certs", $"{certId}.cer");

    public LocalCacheEntry? FindCertCache(string certId) =>
        _entries.FirstOrDefault(e => e.Type == "cert" && e.CloudId == certId && !e.IsExpired);

    public LocalCacheEntry? FindCertCacheByName(string certName) =>
        _entries.FirstOrDefault(e => e.Type == "cert" && e.Name == certName && !e.IsExpired);

    public void CacheCert(CloudCertInfo cert, string localPath)
    {
        // Remove old entries for same cloud ID
        _entries.RemoveAll(e => e.Type == "cert" && e.CloudId == cert.Id);

        var entry = new LocalCacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = "cert",
            Name = cert.CertName,
            CloudId = cert.Id,
            LocalPath = localPath,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = cert.ExpireTimeUtc
        };
        _entries.Add(entry);
        SaveIndex();
    }

    public void CacheCertFile(string certId, string certName, string localPath, DateTime expiresAt)
    {
        _entries.RemoveAll(e => e.Type == "cert" && e.CloudId == certId);

        var entry = new LocalCacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = "cert",
            Name = certName,
            CloudId = certId,
            LocalPath = localPath,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };
        _entries.Add(entry);
        SaveIndex();
    }

    #endregion

    #region Profile缓存

    public string GetProfileCachePath(string profileId, string packageName) =>
        Path.Combine(_cacheDir, "profiles", $"{profileId}_{packageName.Replace('.', '_')}.p7b");

    public LocalCacheEntry? FindProfileCache(string packageName) =>
        _entries.FirstOrDefault(e => e.Type == "profile" && e.PackageName == packageName && !e.IsExpired);

    public LocalCacheEntry? FindProfileCacheById(string profileId) =>
        _entries.FirstOrDefault(e => e.Type == "profile" && e.CloudId == profileId && !e.IsExpired);

    public void CacheProfile(CloudProfileInfo profile, string localPath, string packageName)
    {
        _entries.RemoveAll(e => e.Type == "profile" && e.CloudId == profile.Id);

        var entry = new LocalCacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = "profile",
            Name = profile.ProvisionName,
            CloudId = profile.Id,
            LocalPath = localPath,
            PackageName = packageName,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = profile.ExpireTimeUtc
        };
        _entries.Add(entry);
        SaveIndex();
    }

    public void CacheProfileFile(string profileId, string profileName, string localPath, string packageName, DateTime expiresAt)
    {
        _entries.RemoveAll(e => e.Type == "profile" && e.CloudId == profileId);

        var entry = new LocalCacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = "profile",
            Name = profileName,
            CloudId = profileId,
            LocalPath = localPath,
            PackageName = packageName,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };
        _entries.Add(entry);
        SaveIndex();
    }

    #endregion

    #region 密钥库缓存

    public string GetKeystoreCachePath(string name) => Path.Combine(_cacheDir, "keystores", $"{name}.p12");
    public string GetCsrCachePath(string name) => Path.Combine(_cacheDir, "keystores", $"{name}.csr");

    public LocalCacheEntry? FindKeystoreCache(string name) =>
        _entries.FirstOrDefault(e => e.Type == "keystore" && e.Name == name && File.Exists(e.LocalPath));

    public void CacheKeystore(string name, string keystorePath, string csrPath)
    {
        _entries.RemoveAll(e => e.Type == "keystore" && e.Name == name);

        var entry = new LocalCacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = "keystore",
            Name = name,
            LocalPath = keystorePath,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.MaxValue
        };
        _entries.Add(entry);
        SaveIndex();
    }

    #endregion

    #region 查询

    public List<LocalCacheEntry> GetAllEntries() => _entries.ToList();

    public List<LocalCacheEntry> GetEntriesByType(string type) =>
        _entries.Where(e => e.Type == type).ToList();

    public void RemoveEntry(string id)
    {
        _entries.RemoveAll(e => e.Id == id);
        SaveIndex();
    }

    public void ClearExpired()
    {
        var expired = _entries.Where(e => e.IsExpired).ToList();
        foreach (var entry in expired)
        {
            if (File.Exists(entry.LocalPath))
                try { File.Delete(entry.LocalPath); } catch { }
        }
        _entries.RemoveAll(e => e.IsExpired);
        SaveIndex();
    }

    public void ClearAll()
    {
        foreach (var entry in _entries)
        {
            if (File.Exists(entry.LocalPath))
                try { File.Delete(entry.LocalPath); } catch { }
        }
        _entries.Clear();
        SaveIndex();
    }

    #endregion
}
