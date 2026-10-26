using AgroMarket.Api.Common;

namespace AgroMarket.Api.Services;

/// <summary>
/// 图片净化: 校验真实格式并按容器结构剥掉元数据。
///
/// 为什么不是"解码后重新编码": 项目没有引入图像库(SkiaSharp 要带原生库, ImageSharp 新版
/// 是拆分许可), 为了不引入部署与许可负担, 这里在容器层做三件事, 覆盖实际攻击面:
///  1) 按文件头判定真实格式, 且必须与扩展名一致 (只贴 3 字节魔数的假图在这里就露馅);
///  2) 拒绝正文里出现 HTML/SVG/XML 片段 —— 即 polyglot("FF D8 FF" + "&lt;script&gt;")。
///     这类攻击必须靠可解析的文本载荷才能生效, 而真实照片不会出现这些 ASCII 序列;
///  3) 按 JPEG/PNG/GIF/WebP 的段结构剥掉 EXIF/XMP/IPTC/注释 —— 农户拍的照片常带
///     GPS 坐标与设备信息, 原样对外提供等于把地块位置公开。
///
/// 另有静态响应头 nosniff 兜底(见 Program.cs), 浏览器不会把 image/* 嗅探成 HTML。
/// </summary>
internal static class ImageSanitizer
{
    public enum Format { Jpeg, Png, Gif, Webp }

    /// <summary>单个维度上限, 防止构造"声明巨大尺寸"的文件把下游渲染器拖死。</summary>
    private const int MaxDimension = 20000;

    private static readonly string[] ActiveMarkup =
    {
        "<html", "<script", "<svg", "<?xml", "<!doctype", "<iframe", "<body", "javascript:",
    };

    /// <summary>按扩展名对应的真实格式校验并净化; 校验不通过抛 BizException。</summary>
    public static byte[] Sanitize(byte[] data, string ext)
    {
        var actual = Detect(data);
        var expected = ext switch
        {
            ".jpg" or ".jpeg" => Format.Jpeg,
            ".png" => Format.Png,
            ".gif" => Format.Gif,
            ".webp" => Format.Webp,
            _ => throw new BizException("仅支持 jpg/png/gif/webp 图片"),
        };
        if (actual is null || actual != expected)
            throw new BizException("文件内容与扩展名不符, 或不是有效图片");

        if (ContainsActiveMarkup(data))
            throw new BizException("文件内容包含可执行标记, 已拒绝");

        return actual switch
        {
            Format.Jpeg => SanitizeJpeg(data),
            Format.Png => SanitizePng(data),
            Format.Gif => SanitizeGif(data),
            Format.Webp => SanitizeWebp(data),
            _ => throw new BizException("不支持的图片格式"),
        };
    }

    /// <summary>仅做格式与 polyglot 判定(分片合并前用于快速失败, 不看扩展名)。</summary>
    public static bool IsPlausibleImage(byte[] head)
    {
        var f = Detect(head);
        return f is not null && !ContainsActiveMarkup(head);
    }

    private static Format? Detect(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return Format.Jpeg;
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Format.Png;
        if (b.Length >= 6 && (b.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || b.AsSpan(0, 6).SequenceEqual("GIF89a"u8))) return Format.Gif;
        if (b.Length >= 12 && b.AsSpan(0, 4).SequenceEqual("RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return Format.Webp;
        return null;
    }

    private static bool ContainsActiveMarkup(byte[] b)
    {
        // 用 Latin1 保证一字节对一字符, 不会因非法 UTF-8 而漏判
        var text = System.Text.Encoding.Latin1.GetString(b).ToLowerInvariant();
        foreach (var m in ActiveMarkup)
            if (text.Contains(m, StringComparison.Ordinal)) return true;
        return false;
    }

    // ---------- JPEG: 保留 SOI/APP0 与图像段, 丢弃 APP1..APP15(EXIF/XMP/IPTC/ICC) 与注释 ----------

    private static byte[] SanitizeJpeg(byte[] b)
    {
        var outp = new MemoryStream(b.Length);
        outp.Write(b, 0, 2); // SOI
        var i = 2;
        var sawFrame = false;
        var sawEnd = false;

        while (i + 1 < b.Length)
        {
            if (b[i] != 0xFF) throw new BizException("JPEG 结构异常");
            var marker = b[i + 1];
            if (marker == 0xD9) { sawEnd = true; outp.Write(b, i, 2); i += 2; break; }
            if (marker == 0xFF) { i++; continue; }                 // 填充字节
            if (marker is >= 0xD0 and <= 0xD7 or 0x01) { i += 2; continue; } // 无长度的独立标记

            if (i + 4 > b.Length) throw new BizException("JPEG 结构异常");
            var len = (b[i + 2] << 8) | b[i + 3];
            if (len < 2 || i + 2 + len > b.Length) throw new BizException("JPEG 段长度异常");

            var isSof = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isSof)
            {
                sawFrame = true;
                if (len < 8) throw new BizException("JPEG 尺寸段异常");
                var h = (b[i + 5] << 8) | b[i + 6];
                var w = (b[i + 7] << 8) | b[i + 8];
                if (w is < 1 or > MaxDimension || h is < 1 or > MaxDimension)
                    throw new BizException($"图片尺寸超出允许范围(最大 {MaxDimension}px)");
            }

            // 只留 APP0(JFIF) 与图像/量化/霍夫曼等必要段; EXIF(APP1)、XMP(APP1)、ICC(APP2)、IPTC(APP13) 全丢
            var keep = !(marker is >= 0xE1 and <= 0xEF) && marker != 0xFE;
            if (keep) outp.Write(b, i, 2 + len);
            i += 2 + len;

            if (sawFrame && marker == 0xDA)
            {
                // 扫描数据: 直接拷到文件末尾的 EOI 之前
                var eoi = LastIndexOf(b, new byte[] { 0xFF, 0xD9 }, i);
                if (eoi < 0) throw new BizException("JPEG 缺少结束标记");
                outp.Write(b, i, eoi + 2 - i);
                i = eoi + 2;
                sawEnd = true;
                break;
            }
        }

        if (!sawFrame) throw new BizException("JPEG 缺少图像帧信息");
        if (!sawEnd) throw new BizException("JPEG 缺少结束标记");
        return outp.ToArray();
    }

    // ---------- PNG: 保留必需块, 丢弃文本/时间/EXIF 块 ----------

    private static readonly string[] PngDrop = { "tEXt", "zTXt", "iTXt", "eXIf", "tIME" };

    private static byte[] SanitizePng(byte[] b)
    {
        var outp = new MemoryStream(b.Length);
        outp.Write(b, 0, 8);
        var i = 8;
        var first = true;
        var sawEnd = false;

        while (i + 8 <= b.Length)
        {
            var len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
            if (len < 0 || i + 12L + len > b.Length) throw new BizException("PNG 块长度异常");
            var type = System.Text.Encoding.ASCII.GetString(b, i + 4, 4);

            if (first)
            {
                if (type != "IHDR" || len != 13) throw new BizException("PNG 首块必须是 IHDR");
                var w = (b[i + 8] << 24) | (b[i + 9] << 16) | (b[i + 10] << 8) | b[i + 11];
                var h = (b[i + 12] << 24) | (b[i + 13] << 16) | (b[i + 14] << 8) | b[i + 15];
                if (w is < 1 or > MaxDimension || h is < 1 or > MaxDimension)
                    throw new BizException($"图片尺寸超出允许范围(最大 {MaxDimension}px)");
                first = false;
            }

            var drop = PngDrop.Contains(type);
            if (!drop) outp.Write(b, i, 12 + len);
            i += 12 + len;

            if (type == "IEND") { sawEnd = true; break; }
        }

        if (first) throw new BizException("PNG 缺少 IHDR");
        if (!sawEnd) throw new BizException("PNG 缺少 IEND");
        return outp.ToArray();
    }

    // ---------- GIF: 遍历块结构, 丢弃注释(0xFE)与应用扩展(0xFF, 可能是 XMP) ----------

    private static byte[] SanitizeGif(byte[] b)
    {
        if (b.Length < 14) throw new BizException("GIF 结构异常");
        var outp = new MemoryStream(b.Length);
        outp.Write(b, 0, 13); // 头 + 逻辑屏幕描述符
        var i = 13;
        var flags = b[10];
        if ((flags & 0x80) != 0) { var lct = 3 * (1 << ((flags & 0x07) + 1)); i += lct; outp.Write(b, 13, lct); }
        var sawImage = false;
        var sawTrailer = false;

        while (i < b.Length)
        {
            var block = b[i];
            if (block == 0x3B) { outp.WriteByte(0x3B); sawTrailer = true; i++; break; }

            if (block == 0x21 && i + 1 < b.Length)
            {
                var label = b[i + 1];
                var start = i;
                var j = i + 2;
                while (j < b.Length && b[j] != 0) { j += b[j] + 1; }
                if (j >= b.Length) throw new BizException("GIF 扩展块异常");
                j++; // 块终止符
                // 0xFE 注释 / 0xFF 应用扩展(含 XMP) 丢弃
                if (label is not (0xFE or 0xFF)) outp.Write(b, start, j - start);
                i = j;
                continue;
            }

            if (block == 0x2C && i + 10 <= b.Length)
            {
                var start = i;
                var lflags = b[i + 9];
                var j = i + 10;
                if ((lflags & 0x80) != 0) j += 3 * (1 << ((lflags & 0x07) + 1));
                if (j >= b.Length) throw new BizException("GIF 图像块异常");
                j++; // LZW 最小码长
                while (j < b.Length && b[j] != 0) { j += b[j] + 1; }
                if (j >= b.Length) throw new BizException("GIF 图像数据异常");
                j++;
                outp.Write(b, start, j - start);
                sawImage = true;
                i = j;
                continue;
            }

            throw new BizException("GIF 结构异常");
        }

        if (!sawImage) throw new BizException("GIF 缺少图像数据");
        if (!sawTrailer) throw new BizException("GIF 缺少结束标记");
        return outp.ToArray();
    }

    // ---------- WebP: 丢弃 EXIF / XMP 块, 并清掉 VP8X 里的对应标志位 ----------

    private static byte[] SanitizeWebp(byte[] b)
    {
        var outp = new MemoryStream(b.Length);
        outp.Write(b, 0, 12); // RIFF + size + WEBP
        var i = 12;
        var sawImage = false;

        while (i + 8 <= b.Length)
        {
            var fourcc = System.Text.Encoding.ASCII.GetString(b, i, 4);
            var len = b[i + 4] | (b[i + 5] << 8) | (b[i + 6] << 16) | (b[i + 7] << 24);
            if (len < 0 || i + 8L + len > b.Length) throw new BizException("WebP 块长度异常");
            var padded = len + (len & 1);

            if (fourcc is "VP8 " or "VP8L" or "VP8X") sawImage = true;

            if (fourcc is "EXIF" or "XMP ")
            {
                i += 8 + padded; // 直接丢弃
                continue;
            }

            if (fourcc == "VP8X" && len >= 10)
            {
                outp.Write(b, i, 8);
                var chunk = b.AsSpan(i + 8, len).ToArray();
                chunk[0] &= 0b1110_0011; // 清 EXIF(bit3) 与 XMP(bit2) 标志
                outp.Write(chunk, 0, chunk.Length);
                if ((len & 1) == 1) outp.WriteByte(0);
                i += 8 + padded;
                continue;
            }

            outp.Write(b, i, 8 + padded);
            i += 8 + padded;
        }

        if (!sawImage) throw new BizException("WebP 缺少图像数据");

        // RIFF 头部 size 需要按新长度回填(不含前 8 字节)
        var result = outp.ToArray();
        var riffSize = result.Length - 8;
        result[4] = (byte)(riffSize & 0xFF);
        result[5] = (byte)((riffSize >> 8) & 0xFF);
        result[6] = (byte)((riffSize >> 16) & 0xFF);
        result[7] = (byte)((riffSize >> 24) & 0xFF);
        return result;
    }

    private static int LastIndexOf(byte[] b, byte[] pattern, int startAt)
    {
        for (var i = b.Length - pattern.Length; i >= startAt; i--)
        {
            var match = true;
            for (var k = 0; k < pattern.Length; k++)
                if (b[i + k] != pattern[k]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
