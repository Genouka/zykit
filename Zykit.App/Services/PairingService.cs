using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Zykit.App.Json;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class PairingService
{
    private readonly ISdkPathProvider _sdk;
    private readonly LocalCacheService _cache;
    private readonly string _pairFile;
    private List<SigningPair> _pairs = new();

    public PairingService(ISdkPathProvider sdk, LocalCacheService cache)
    {
        _sdk = sdk;
        _cache = cache;
        _pairFile = Path.Combine(_sdk.ConfigDir, "signing-pairs.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_pairFile)) return;
        try
        {
            var json = File.ReadAllText(_pairFile);
            _pairs = JsonSerializer.Deserialize(json, ZykitJsonContext.Default.ListSigningPair) ?? new();
        }
        catch { _pairs = new(); }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_pairs, ZykitJsonContext.Default.ListSigningPair);
        File.WriteAllText(_pairFile, json);
    }

    /// <summary>查找包名对应的有效搭配</summary>
    public SigningPair? FindValidPair(string packageName, string? appId = null) =>
        _pairs.FirstOrDefault(p => p.PackageName == packageName && (appId == null || p.AppId == appId) && p.IsValid);

    /// <summary>查找包名对应的搭配（含过期的）</summary>
    public SigningPair? FindPair(string packageName, string? appId = null) =>
        _pairs.FirstOrDefault(p => p.PackageName == packageName && (appId == null || p.AppId == appId));

    /// <summary>获取所有搭配</summary>
    public List<SigningPair> GetAllPairs() => _pairs.ToList();

    /// <summary>保存或更新搭配</summary>
    public void SavePair(SigningPair pair)
    {
        var existing = _pairs.FirstOrDefault(p => p.Id == pair.Id);
        if (existing != null)
        {
            var idx = _pairs.IndexOf(existing);
            _pairs[idx] = pair;
        }
        else
        {
            // 同包名+同AppId只保留一个
            _pairs.RemoveAll(p => p.PackageName == pair.PackageName && p.AppId == pair.AppId);
            _pairs.Add(pair);
        }
        Save();
    }

    /// <summary>删除搭配</summary>
    public void DeletePair(string pairId)
    {
        _pairs.RemoveAll(p => p.Id == pairId);
        Save();
    }

    /// <summary>更新搭配的最后使用时间</summary>
    public void TouchPair(string pairId)
    {
        var pair = _pairs.FirstOrDefault(p => p.Id == pairId);
        if (pair != null)
        {
            pair.LastUsedAt = DateTime.UtcNow;
            Save();
        }
    }

    /// <summary>清理所有过期搭配</summary>
    public void ClearExpiredPairs()
    {
        _pairs.RemoveAll(p => !p.IsValid);
        Save();
    }
}
