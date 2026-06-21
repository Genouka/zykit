using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class KeyStoreService
{
    private readonly ISdkPathProvider _sdk;
    private readonly SignService _signService;

    public KeyStoreService(ISdkPathProvider sdk, SignService signService)
    {
        _sdk = sdk;
        _signService = signService;
    }

    public string GetKeystorePath(string name = "AUTO_ZYKIT") =>
        Path.Combine(_sdk.ConfigDir, $"{name}.p12");

    public string GetCsrPath(string name = "AUTO_ZYKIT") =>
        Path.Combine(_sdk.ConfigDir, $"{name}.csr");

    public string GetCertPath(string name = "AUTO_ZYKIT") =>
        Path.Combine(_sdk.ConfigDir, $"{name}.cer");

    public string GetProfilePath(string packageName, string name = "AUTO_ZYKIT") =>
        Path.Combine(_sdk.ConfigDir, $"{name}_{packageName.Replace('.', '_')}.p7b");

    public string GetSignedOutputPath(string hapPath) =>
        Path.Combine(_sdk.ConfigDir, "signed", Path.GetFileName(hapPath));

    public async Task<(bool success, string message)> EnsureKeystoreAndCsrAsync(string name = "AUTO_ZYKIT")
    {
        var keystorePath = GetKeystorePath(name);
        var csrPath = GetCsrPath(name);

        var (ksSuccess, ksMsg) = await _signService.CreateKeystoreAsync(keystorePath);
        if (!ksSuccess) return (false, $"创建密钥库失败: {ksMsg}");

        var (csrSuccess, csrMsg) = await _signService.CreateCsrAsync(keystorePath, csrPath);
        if (!csrSuccess) return (false, $"创建CSR失败: {csrMsg}");

        return (true, "密钥库和CSR已就绪");
    }

    public HapPackage? LoadHapInfo(string hapPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(hapPath);
            var packageName = "";
            var appName = "";
            var moduleJson = "";

            // 策略1: 从 module.json 读取（HAP文件）
            var moduleEntry = zip.GetEntry("module.json");
            if (moduleEntry != null)
            {
                using (var stream = moduleEntry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    moduleJson = reader.ReadToEnd();
                }

                var doc = JsonDocument.Parse(moduleJson);
                if (doc.RootElement.TryGetProperty("app", out var app))
                {
                    packageName = app.TryGetProperty("bundleName", out var bn) ? bn.GetString() ?? "" : "";
                    appName = app.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                }
            }

            // 策略2: 从 pack.info 读取（APP文件和HAP文件都有）
            if (string.IsNullOrEmpty(packageName))
            {
                var packEntry = zip.GetEntry("pack.info");
                if (packEntry != null)
                {
                    using var stream = packEntry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var packJson = reader.ReadToEnd();

                    var doc = JsonDocument.Parse(packJson);
                    if (doc.RootElement.TryGetProperty("summary", out var summary) &&
                        summary.TryGetProperty("app", out var app))
                    {
                        packageName = app.TryGetProperty("bundleName", out var bn) ? bn.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(appName))
                            appName = app.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                    }
                }
            }

            // 策略3: APP文件中内嵌的HAP，从内嵌HAP的module.json读取
            if (string.IsNullOrEmpty(packageName))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith(".hap", StringComparison.OrdinalIgnoreCase))
                    {
                        using var hapStream = entry.Open();
                        using var hapMem = new MemoryStream();
                        hapStream.CopyTo(hapMem);
                        hapMem.Position = 0;

                        using var innerZip = new ZipArchive(hapMem, ZipArchiveMode.Read);
                        var innerModule = innerZip.GetEntry("module.json");
                        if (innerModule != null)
                        {
                            using var ms = innerModule.Open();
                            using var sr = new StreamReader(ms, Encoding.UTF8);
                            moduleJson = sr.ReadToEnd();

                            var doc = JsonDocument.Parse(moduleJson);
                            if (doc.RootElement.TryGetProperty("app", out var app))
                            {
                                packageName = app.TryGetProperty("bundleName", out var bn) ? bn.GetString() ?? "" : "";
                                appName = app.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                            }
                        }
                        if (!string.IsNullOrEmpty(packageName)) break;
                    }
                }
            }

            if (string.IsNullOrEmpty(packageName))
                return null;

            return new HapPackage
            {
                FilePath = hapPath,
                PackageName = packageName,
                AppName = appName,
                FileSize = new FileInfo(hapPath).Length,
                ModuleJson = moduleJson
            };
        }
        catch { return null; }
    }
}
