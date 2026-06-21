using System;
using System.IO;
using System.Threading.Tasks;

namespace Zykit.App.Services;

public class SignService
{
    private readonly ISdkPathProvider _sdk;

    public SignService(ISdkPathProvider sdk)
    {
        _sdk = sdk;
    }

    /// <summary>生成密钥对到 P12 密钥库（纯 C#，替代 hap-sign-tool generate-keypair）</summary>
    public async Task<(bool success, string message)> CreateKeystoreAsync(string keystorePath, string storepass = "zykit123", string alias = "zykit", string cn = "zykit")
    {
        if (CryptoHelper.KeystoreContainsAlias(keystorePath, storepass, alias))
            return (true, "密钥库已存在且别名可用");

        if (File.Exists(keystorePath))
        {
            try { File.Delete(keystorePath); } catch { }
        }

        try
        {
            await Task.Run(() => CryptoHelper.CreateKeystore(keystorePath, storepass, alias, cn));
            if (File.Exists(keystorePath))
                return (true, "密钥库创建成功");
            return (false, "密钥库创建失败: 文件未生成");
        }
        catch (Exception ex)
        {
            return (false, $"创建密钥库失败: {ex.Message}");
        }
    }

    /// <summary>从密钥库生成 CSR（纯 C#，替代 hap-sign-tool generate-csr）</summary>
    public async Task<(bool success, string message)> CreateCsrAsync(string keystorePath, string csrPath, string alias = "zykit", string storepass = "zykit123")
    {
        if (File.Exists(csrPath))
            return (true, csrPath);

        try
        {
            await Task.Run(() => CryptoHelper.CreateCsr(
                keystorePath, csrPath, storepass, alias,
                "C=CN,O=HUAWEI,OU=HUAWEI IDE,CN=zykit"));
            if (File.Exists(csrPath))
                return (true, csrPath);
            return (false, "CSR 生成失败: 文件未生成");
        }
        catch (Exception ex)
        {
            return (false, $"生成 CSR 失败: {ex.Message}");
        }
    }

    /// <summary>对 HAP 文件签名（纯 C#，替代 hap-sign-tool sign-app）</summary>
    public async Task<(bool success, string message)> SignHapAsync(
        string keystoreFile, string keystorePwd, string keyAlias,
        string certFile, string profileFile, string inFile, string outFile)
    {
        if (!File.Exists(keystoreFile)) return (false, $"密钥库不存在: {keystoreFile}");
        if (!File.Exists(certFile)) return (false, $"证书不存在: {certFile}");
        if (!File.Exists(profileFile)) return (false, $"Profile不存在: {profileFile}");
        if (!File.Exists(inFile)) return (false, $"HAP文件不存在: {inFile}");

        var outDir = Path.GetDirectoryName(outFile);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        try
        {
            await Task.Run(() =>
            {
                // 1. 从密钥库加载私钥与证书
                var (privateKey, ksCert) = CryptoHelper.LoadSigningKey(keystoreFile, keystorePwd, keyAlias);

                // 2. 加载应用证书（.cer，由 AGC 颁发，作为签名证书）
                var appCert = LoadCertificate(certFile);

                // 3. 加载 Profile（.p7b 原始字节）
                var profileBytes = File.ReadAllBytes(profileFile);

                // 4. 签名 HAP
                HapSigner.Sign(inFile, outFile, privateKey, appCert, profileBytes);
            });

            if (!File.Exists(outFile)) return (false, "签名文件未生成");

            var outSize = new FileInfo(outFile).Length;
            var inSize = new FileInfo(inFile).Length;
            if (outSize < inSize * 0.9) return (false, $"签名文件大小异常: 输出{outSize}字节, 输入{inSize}字节");

            return (true, $"签名成功: {outFile}");
        }
        catch (Exception ex)
        {
            return (false, $"签名失败: {ex.Message}");
        }
    }

    public string ReadCsr(string csrPath) => File.ReadAllText(csrPath);

    private static Org.BouncyCastle.X509.X509Certificate LoadCertificate(string certFile)
    {
        var parser = new Org.BouncyCastle.X509.X509CertificateParser();
        using var fs = File.OpenRead(certFile);
        return parser.ReadCertificate(fs);
    }
}
