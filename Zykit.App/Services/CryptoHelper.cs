using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using X509Cert = Org.BouncyCastle.X509.X509Certificate;

namespace Zykit.App.Services;

/// <summary>
/// 纯 C# 实现的密钥库/CSR 操作，替代 hap-sign-tool.jar 的 generate-keypair / generate-csr。
/// </summary>
internal static class CryptoHelper
{
    /// <summary>生成 ECC(NIST-P-256) 密钥对并写入 PKCS12 密钥库（含自签证书占位）。</summary>
    public static void CreateKeystore(string keystorePath, string storepass, string alias, string cn)
    {
        var dir = Path.GetDirectoryName(keystorePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 1. 生成 P-256 密钥对
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(X9ObjectIdentifiers.Prime256v1, new SecureRandom()));
        var keyPair = gen.GenerateKeyPair();

        // 2. 生成自签证书（PKCS12 必须有证书链占位）
        var cert = GenerateSelfSignedCert(keyPair, cn);

        // 3. 写入 PKCS12
        var store = new Pkcs12StoreBuilder().Build();
        var certEntry = new X509CertificateEntry(cert);
        store.SetKeyEntry(alias, new AsymmetricKeyEntry(keyPair.Private), new[] { certEntry });
        store.SetCertificateEntry(alias, certEntry);

        using var fs = File.Create(keystorePath);
        store.Save(fs, storepass.ToCharArray(), new SecureRandom());
    }

    /// <summary>验证密钥库是否包含指定别名。</summary>
    public static bool KeystoreContainsAlias(string keystorePath, string storepass, string alias)
    {
        if (!File.Exists(keystorePath)) return false;
        try
        {
            var store = LoadKeystore(keystorePath, storepass);
            return store.GetKey(alias) != null;
        }
        catch { return false; }
    }

    /// <summary>从密钥库生成 PKCS10 CSR。</summary>
    public static void CreateCsr(string keystorePath, string csrPath, string storepass, string alias, string subject)
    {
        var dir = Path.GetDirectoryName(csrPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var store = LoadKeystore(keystorePath, storepass);
        var keyEntry = store.GetKey(alias) ?? throw new InvalidOperationException($"密钥库中未找到别名: {alias}");
        var certEntry = store.GetCertificate(alias) ?? throw new InvalidOperationException($"密钥库中未找到证书: {alias}");

        var privateKey = keyEntry.Key;
        var publicKey = certEntry.Certificate.GetPublicKey();

        var dn = new X509Name(subject);
        var csr = new Pkcs10CertificationRequest("SHA256withECDSA", dn, publicKey, null, privateKey);

        using var fs = File.Create(csrPath);
        var bytes = csr.GetEncoded();
        fs.Write(bytes, 0, bytes.Length);
    }

    /// <summary>从密钥库加载私钥与证书，用于 HAP 签名。</summary>
    public static (AsymmetricKeyParameter privateKey, X509Cert cert) LoadSigningKey(
        string keystorePath, string storepass, string alias)
    {
        var store = LoadKeystore(keystorePath, storepass);
        var keyEntry = store.GetKey(alias) ?? throw new InvalidOperationException($"密钥库中未找到别名: {alias}");
        var certEntry = store.GetCertificate(alias) ?? throw new InvalidOperationException($"密钥库中未找到证书: {alias}");
        return (keyEntry.Key, certEntry.Certificate);
    }

    private static Pkcs12Store LoadKeystore(string path, string password)
    {
        var store = new Pkcs12StoreBuilder().Build();
        using var fs = File.OpenRead(path);
        store.Load(fs, password.ToCharArray());
        return store;
    }

    private static X509Cert GenerateSelfSignedCert(AsymmetricCipherKeyPair keyPair, string cn)
    {
        var gen = new X509V3CertificateGenerator();
        var name = new X509Name($"C=CN,O=HUAWEI,OU=HUAWEI IDE,CN={cn}");
        gen.SetSerialNumber(BigInteger.One);
        gen.SetIssuerDN(name);
        gen.SetSubjectDN(name);
        gen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        gen.SetNotAfter(DateTime.UtcNow.AddDays(9125));
        gen.SetPublicKey(keyPair.Public);
        return gen.Generate(new Asn1SignatureFactory("SHA256withECDSA", keyPair.Private, new SecureRandom()));
    }
}
