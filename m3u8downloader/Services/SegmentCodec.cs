using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using m3u8downloader.Models;

namespace m3u8downloader.Services;

/// <summary>
/// 分片数据处理：AES-128/192/256-CBC 自动解密、按序列号推导 IV、
/// 自动识别并剥离伪装成 PNG/JPG/BMP/GIF/WebP 的图片头，还原真实 TS / fMP4 流。
/// </summary>
public static class SegmentCodec
{
    private const int TsPacketSize = 188;
    private const int MaxHeaderScan = 64 * 1024;

    /// <summary>密钥位数由密钥长度自动决定（16/24/32 → AES-128/192/256）。</summary>
    public static byte[] NormalizeKey(byte[] raw, EncryptionInfo? key)
    {
        if (raw.Length is 16 or 24 or 32) return raw;

        var declared = key?.DeclaredKeySize;
        var size = declared.HasValue
            ? declared.Value / 8
            : raw.Length switch { >= 32 => 32, >= 24 => 24, _ => 16 };
        if (size is not (16 or 24 or 32)) size = 16;

        var result = new byte[size];
        Array.Copy(raw, result, Math.Min(raw.Length, size));
        return result;
    }

    /// <summary>IV 未声明时按 HLS 规范使用分片绝对序列号（大端 16 字节）。</summary>
    public static byte[] NormalizeIv(byte[]? iv, long sequenceNumber)
    {
        var result = new byte[16];
        if (iv is { Length: > 0 })
        {
            Array.Copy(iv, result, Math.Min(iv.Length, 16));
        }
        else
        {
            for (var i = 0; i < 8; i++)
                result[15 - i] = (byte)((sequenceNumber >> (i * 8)) & 0xFF);
        }
        return result;
    }

    /// <summary>
    /// AES-CBC 解密。若分片前被塞入图片头导致解密失败，会自动尝试剥离前缀后重试。
    /// </summary>
    public static byte[] Decrypt(byte[] key, byte[] iv, byte[] data)
    {
        var firstOk = TryDecrypt(key, iv, data, out var plain);
        if (firstOk && LooksLikeMedia(plain)) return plain;

        // 首轮结果不像媒体流时，尝试剥离图片/自定义前缀后重新解密
        var tried = 0;
        foreach (var offset in DisguiseOffsets(data))
        {
            if (offset <= 0 || offset >= data.Length || ++tried > 40) break;
            if (TryDecrypt(key, iv, data[offset..], out var candidate) && LooksLikeMedia(candidate))
                return candidate;
        }

        if (firstOk) return plain;
        throw new CryptographicException("AES 解密失败：密钥或 IV 不正确，或分片使用了不受支持的加密方式");
    }

    /// <summary>
    /// 净化未加密分片：把「图片头 + TS/fMP4」还原为纯 TS/fMP4。
    /// 已经是 TS / fMP4 的分片原样返回。
    /// </summary>
    public static (byte[] Data, bool Repaired) Sanitize(byte[] data)
    {
        if (data.Length == 0) return (data, false);
        if (StartsWithTs(data, 0) || StartsWithFmp4(data, 0)) return (data, false);

        var limit = Math.Min(MaxHeaderScan, data.Length);
        for (var offset = 1; offset + TsPacketSize * 2 < limit; offset++)
        {
            if (!StartsWithTs(data, offset)) continue;
            return (data[offset..], true);
        }

        for (var offset = 1; offset + 12 < limit; offset++)
        {
            if (!StartsWithFmp4(data, offset)) continue;
            return (data[offset..], true);
        }

        // 已知图片头：直接跳到头部之后（适用于图片头后紧跟密文/TS 的站点）
        foreach (var headerLength in ImageHeaderLengths(data))
        {
            if (headerLength <= 0 || headerLength >= data.Length) continue;
            return (data[headerLength..], true);
        }

        return (data, false);
    }

    /// <summary>加密分片只按已知图片头剥离，避免对密文误判 TS 同步字节。</summary>
    public static (byte[] Data, bool Repaired) SanitizeCipher(byte[] data)
    {
        if (data.Length == 0) return (data, false);
        foreach (var headerLength in ImageHeaderLengths(data))
        {
            if (headerLength <= 0 || headerLength >= data.Length) continue;
            if ((data.Length - headerLength) % 16 != 0) continue;
            return (data[headerLength..], true);
        }
        return (data, false);
    }

    private static bool TryDecrypt(byte[] key, byte[] iv, byte[] data, out byte[] result)
    {
        result = [];
        if (data.Length == 0 || data.Length % 16 != 0) return false;
        try
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            result = decryptor.TransformFinalBlock(data, 0, data.Length);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>伪装前缀候选：先试已知图片格式，再按 AES 块对齐扫描任意小前缀。</summary>
    private static IEnumerable<int> DisguiseOffsets(byte[] data)
    {
        foreach (var length in ImageHeaderLengths(data))
            if (length > 0 && length < data.Length)
                yield return length;

        var max = Math.Min(1024, data.Length - 16);
        for (var offset = 1; offset <= max; offset++)
            if ((data.Length - offset) % 16 == 0)
                yield return offset;
    }

    private static IEnumerable<int> ImageHeaderLengths(byte[] data)
    {
        // PNG 签名 8 字节
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            yield return 8;

        // JPEG：优先跳到 EOI 之后（完整图片 + TS 的伪装方式），其次仅跳过 SOI
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var scanLimit = Math.Min(data.Length - 1, MaxHeaderScan);
            for (var i = 2; i < scanLimit; i++)
            {
                if (data[i] == 0xFF && data[i + 1] == 0xD9)
                {
                    yield return i + 2;
                    break;
                }
            }
            yield return 2;
            yield return 4;
        }

        // BMP：文件头中记录了图片长度
        if (data.Length >= 6 && data[0] == 'B' && data[1] == 'M')
        {
            var declared = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(2, 4));
            if (declared > 0 && declared < data.Length) yield return declared;
            yield return 2;
            yield return 14;
            yield return 54;
        }

        // GIF87a / GIF89a
        if (data.Length >= 13 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
        {
            yield return 13;
            yield return 6;
        }

        // WebP：RIFF....WEBP
        if (data.Length >= 12 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(data, 8, 4) == "WEBP")
        {
            var declared = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4)) + 8;
            if (declared > 0 && declared < data.Length) yield return declared;
            yield return 12;
        }
    }

    private static bool LooksLikeMedia(byte[] data)
    {
        if (data.Length < 8) return false;
        if (StartsWithTs(data, 0) || StartsWithFmp4(data, 0)) return true;

        // ISO BMFF：分片通常是 moof/mdat，初始化段是 ftyp/moov
        var box = Encoding.ASCII.GetString(data, 4, 4);
        if (box is "ftyp" or "styp" or "moof" or "moov" or "sidx" or "mdat") return true;
        // ADTS / ID3（纯音频流）
        if (data[0] == 0xFF && (data[1] & 0xF0) == 0xF0) return true;
        if (Encoding.ASCII.GetString(data, 0, 3) == "ID3") return true;
        return false;
    }

    private static bool StartsWithTs(byte[] data, int offset)
    {
        if (offset < 0 || offset + TsPacketSize * 2 >= data.Length) return false;
        return data[offset] == 0x47
               && data[offset + TsPacketSize] == 0x47
               && data[offset + TsPacketSize * 2] == 0x47;
    }

    private static bool StartsWithFmp4(byte[] data, int offset)
    {
        if (offset < 0 || offset + 12 > data.Length) return false;
        return data[offset + 4] == 'f' && data[offset + 5] == 't'
               && data[offset + 6] == 'y' && data[offset + 7] == 'p';
    }
}
