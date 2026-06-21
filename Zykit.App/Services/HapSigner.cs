using System;
using System.IO;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Zykit.App.Services;

/// <summary>
/// 纯 C# 实现的 HarmonyOS HAP 签名，替代 hap-sign-tool.jar sign-app。
/// 签名块格式参照 OpenHarmony developtools_hapsigner。
/// </summary>
internal static class HapSigner
{
    // 签名块尾部 header 大小：blockCount(4) + size(8) + magic(16) + version(4)
    private const int SigBlockHeaderSize = 32;
    // 每个 sub-block TLV 头：type(4) + length(4) + offset(4)
    private const int SubBlockHeaderSize = 12;
    // chunk 最大 1MB
    private const int ChunkMaxSize = 1024 * 1024;
    // 摘要列表编码常量
    private const uint ContentVersion = 2;
    private const uint BlockNumber = 1;
    // chunk 摘要前缀 / 顶层摘要前缀
    private const byte ChunkPrefix = 0xA5;
    private const byte TopPrefix = 0x5A;

    // sub-block 类型
    private const uint ProfileBlockId = 0x20000002;
    private const uint SignatureBlockId = 0x20000000;

    // 签名算法 ID（ECDSA-SHA256）
    private const uint AlgorithmIdEcdsaSha256 = 0x201;

    // V2 魔数 "HAP Sig Block 42"
    private static readonly byte[] MagicV2 =
    {
        0x48, 0x41, 0x50, 0x20, 0x53, 0x69, 0x67, 0x20,
        0x42, 0x6c, 0x6f, 0x63, 0x6b, 0x20, 0x34, 0x32
    };

    private const uint SchemaVersion = 2;

    /// <summary>
    /// 对 HAP 文件进行签名。
    /// </summary>
    public static void Sign(
        string inFile, string outFile,
        AsymmetricKeyParameter privateKey, X509Certificate cert,
        byte[] profileBytes)
    {
        var hapBytes = File.ReadAllBytes(inFile);
        var signed = SignBytes(hapBytes, privateKey, cert, profileBytes);
        File.WriteAllBytes(outFile, signed);
    }

    private static byte[] SignBytes(
        byte[] hap, AsymmetricKeyParameter privateKey, X509Certificate cert, byte[] profileBytes)
    {
        // 1. 解析 ZIP 结构，定位 EOCD / 中央目录
        int eocdOffset = FindEocd(hap);
        int cdOffset = (int)ReadUInt32LE(hap, eocdOffset + 16);
        int cdSize = (int)ReadUInt32LE(hap, eocdOffset + 12);

        // 2. 计算三段数据的分块摘要
        //    段1: [0, cdOffset)  段2: [cdOffset, cdOffset+cdSize)  段3: EOCD（CD offset 字段保持原始值）
        byte[] eocdForDigest = BuildEocdForDigest(hap, eocdOffset, cdOffset);
        byte[] topDigest = ComputeTopDigest(hap, 0, cdOffset, hap, cdOffset, cdSize, eocdForDigest, profileBytes);

        // 3. 编码摘要列表（作为 PKCS7 content）
        byte[] encodedDigests = EncodeDigestList(AlgorithmIdEcdsaSha256, topDigest);

        // 4. 生成 PKCS7/CMS 签名
        byte[] pkcs7 = GeneratePkcs7(encodedDigests, privateKey, cert);

        // 5. 组装签名块（sub-blocks: profile + signature）
        byte[] sigBlock = BuildSigningBlock(profileBytes, pkcs7);

        // 6. 拼接输出：段1 + 签名块 + 中央目录 + 更新偏移后的 EOCD
        return AssembleOutput(hap, cdOffset, cdSize, eocdOffset, sigBlock);
    }

    /// <summary>从尾部扫描 EOCD 签名 0x06054b50。</summary>
    private static int FindEocd(byte[] data)
    {
        // EOCD 最小 22 字节，最大带注释 22 + 65535
        int minPos = Math.Max(0, data.Length - 22 - 65535);
        for (int i = data.Length - 22; i >= minPos; i--)
        {
            if (data[i] == 0x50 && data[i + 1] == 0x4b && data[i + 2] == 0x05 && data[i + 3] == 0x06)
                return i;
        }
        throw new InvalidDataException("未找到 ZIP EOCD 记录");
    }

    private static uint ReadUInt32LE(byte[] data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }

    private static void WriteUInt32LE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt64LE(byte[] data, int offset, long value)
    {
        for (int i = 0; i < 8; i++)
            data[offset + i] = (byte)(value >> (8 * i));
    }

    /// <summary>构造参与摘要的 EOCD：副本，CD offset 字段保持为原始 cdOffset。</summary>
    private static byte[] BuildEocdForDigest(byte[] hap, int eocdOffset, int cdOffset)
    {
        int eocdLen = hap.Length - eocdOffset;
        byte[] eocd = new byte[eocdLen];
        Array.Copy(hap, eocdOffset, eocd, 0, eocdLen);
        // EOCD 偏移 16 处的 CD offset 字段已是原始 cdOffset，无需修改
        return eocd;
    }

    /// <summary>
    /// 计算顶层摘要。
    /// 输入 = 0x5A || chunkCount(LE) || 各 chunk 摘要 || optional blocks(profile) value
    /// </summary>
    private static byte[] ComputeTopDigest(
        byte[] seg1, int seg1Off, int seg1Len,
        byte[] seg2, int seg2Off, int seg2Len,
        byte[] seg3, byte[] profileBytes)
    {
        using var sha = SHA256.Create();
        // 顶层前缀
        sha.TransformBlock(new byte[] { TopPrefix }, 0, 1, null, 0);

        // 先计算所有 chunk 摘要，再写入 chunkCount
        var chunkDigests = ComputeChunkDigests(seg1, seg1Off, seg1Len, seg2, seg2Off, seg2Len, seg3);
        byte[] countBytes = BitConverter.GetBytes((uint)chunkDigests.Count);
        sha.TransformBlock(countBytes, 0, 4, null, 0);
        foreach (var d in chunkDigests)
            sha.TransformBlock(d, 0, d.Length, null, 0);

        // 追加 optional blocks 的 value（profile）
        byte[] finalHash;
        if (profileBytes != null && profileBytes.Length > 0)
        {
            sha.TransformBlock(profileBytes, 0, profileBytes.Length, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            finalHash = sha.Hash!;
        }
        else
        {
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            finalHash = sha.Hash!;
        }
        return finalHash;
    }

    /// <summary>对三段数据顺序拼接后按 1MB 分块，每块算 SHA256(0xA5 || size(LE) || data)。</summary>
    private static System.Collections.Generic.List<byte[]> ComputeChunkDigests(
        byte[] seg1, int seg1Off, int seg1Len,
        byte[] seg2, int seg2Off, int seg2Len,
        byte[] seg3)
    {
        var digests = new System.Collections.Generic.List<byte[]>();
        using var sha = SHA256.Create();
        // 用游标遍历三段
        var segments = new (byte[] buf, int off, int len)[]
        {
            (seg1, seg1Off, seg1Len),
            (seg2, seg2Off, seg2Len),
            (seg3, 0, seg3.Length)
        };

        byte[] chunk = new byte[ChunkMaxSize];
        int chunkFill = 0;
        foreach (var (buf, off, len) in segments)
        {
            int pos = 0;
            while (pos < len)
            {
                int toCopy = Math.Min(ChunkMaxSize - chunkFill, len - pos);
                Array.Copy(buf, off + pos, chunk, chunkFill, toCopy);
                chunkFill += toCopy;
                pos += toCopy;
                if (chunkFill == ChunkMaxSize)
                {
                    digests.Add(HashChunk(chunk, chunkFill, sha));
                    chunkFill = 0;
                }
            }
        }
        // 尾部不足 1MB 的块
        if (chunkFill > 0)
            digests.Add(HashChunk(chunk, chunkFill, sha));

        return digests;
    }

    private static byte[] HashChunk(byte[] chunk, int size, SHA256 sha)
    {
        // SHA256(0xA5 || uint32_LE(size) || chunkData)
        sha.Initialize();
        sha.TransformBlock(new byte[] { ChunkPrefix }, 0, 1, null, 0);
        byte[] sizeBytes = BitConverter.GetBytes((uint)size);
        sha.TransformBlock(sizeBytes, 0, 4, null, 0);
        sha.TransformBlock(chunk, 0, size, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return sha.Hash!;
    }

    /// <summary>编码摘要列表：CONTENT_VERSION || BLOCK_NUMBER || (pairSize || algoId || digestLen || digest)。</summary>
    private static byte[] EncodeDigestList(uint algorithmId, byte[] digest)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(ContentVersion);
        bw.Write(BlockNumber);
        uint pairSize = 4 + 4 + (uint)digest.Length;
        bw.Write(pairSize);
        bw.Write(algorithmId);
        bw.Write((uint)digest.Length);
        bw.Write(digest);
        return ms.ToArray();
    }

    /// <summary>生成 PKCS7/CMS SignedData，content 为编码后的摘要列表。</summary>
    private static byte[] GeneratePkcs7(byte[] encodedDigests, AsymmetricKeyParameter privateKey, X509Certificate cert)
    {
        var gen = new CmsSignedDataGenerator();
        // DigestSha256 + EC 私钥 => SHA256withECDSA
        gen.AddSigner(privateKey, cert, CmsSignedGenerator.DigestSha256);
        gen.AddCertificate(cert);
        var content = new CmsProcessableByteArray(encodedDigests);
        var signedData = gen.Generate(content, encapsulate: true);
        return signedData.GetEncoded();
    }

    /// <summary>
    /// 组装签名块：sub-block TLV 表项 + values + 尾部 header。
    /// sub-blocks 顺序：profile(可选) -> signature。
    /// </summary>
    private static byte[] BuildSigningBlock(byte[] profileBytes, byte[] pkcs7)
    {
        var subBlocks = new System.Collections.Generic.List<(uint type, byte[] value)>();
        if (profileBytes != null && profileBytes.Length > 0)
            subBlocks.Add((ProfileBlockId, profileBytes));
        subBlocks.Add((SignatureBlockId, pkcs7));

        int n = subBlocks.Count;
        int tlvSize = n * SubBlockHeaderSize;
        int valuesSize = 0;
        foreach (var (_, v) in subBlocks) valuesSize += v.Length;
        int totalSize = tlvSize + valuesSize + SigBlockHeaderSize;

        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        // 1. TLV 表项
        int currentOffset = tlvSize;
        foreach (var (type, value) in subBlocks)
        {
            bw.Write(type);
            bw.Write((uint)value.Length);
            bw.Write((uint)currentOffset);
            currentOffset += value.Length;
        }
        // 2. values
        foreach (var (_, value) in subBlocks)
            bw.Write(value);
        // 3. 尾部 header
        bw.Write((uint)n);              // blockCount
        bw.Write((long)totalSize);      // size (int64 LE)
        bw.Write(MagicV2);              // magic
        bw.Write(SchemaVersion);        // version
        return ms.ToArray();
    }

    /// <summary>拼接最终输出：段1 + 签名块 + 中央目录 + 更新 CD offset 后的 EOCD。</summary>
    private static byte[] AssembleOutput(byte[] hap, int cdOffset, int cdSize, int eocdOffset, byte[] sigBlock)
    {
        int eocdLen = hap.Length - eocdOffset;
        long newCdOffset = (long)cdOffset + sigBlock.Length;

        var ms = new MemoryStream();
        // 段1：所有 local file entries
        ms.Write(hap, 0, cdOffset);
        // 签名块
        ms.Write(sigBlock, 0, sigBlock.Length);
        // 中央目录
        ms.Write(hap, cdOffset, cdSize);
        // EOCD（更新 CD offset 字段）
        byte[] eocd = new byte[eocdLen];
        Array.Copy(hap, eocdOffset, eocd, 0, eocdLen);
        WriteUInt32LE(eocd, 16, (uint)newCdOffset);
        ms.Write(eocd, 0, eocdLen);
        return ms.ToArray();
    }
}
