using System.Text.Json;
using Dapper;
using AgroMarket.Api.Common;

namespace AgroMarket.Api.Services;

/// <summary>本地上传结果: 分片上传初始化信息。</summary>
public record UploadInitResult(string UploadId, int ChunkSize, int TotalChunks, List<int> UploadedChunks);

/// <summary>分片上传状态。</summary>
public record UploadStatus(string UploadId, string FileName, long FileSize, int ChunkSize, int TotalChunks, List<int> UploadedChunks);

/// <summary>
/// 本地文件存储, 支持单文件与分片(断点续传)上传。
/// 只收图片: 扩展名白名单 + 真实格式判定 + 结构校验 + 元数据剥离(ImageSanitizer)。
/// 分片会话绑定发起人(userId), 并对每个用户限制并发会话数与临时目录占用。
/// </summary>
public class StorageService
{
    private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private static readonly TimeSpan TmpTtl = TimeSpan.FromHours(24);
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    private readonly IDbConnectionFactory _db;

    public StorageService(IConfiguration cfg, IWebHostEnvironment env, IDbConnectionFactory db)
    { _cfg = cfg; _env = env; _db = db; }
    private string Root => Path.Combine(_env.ContentRootPath, _cfg["Storage:LocalRoot"] ?? "uploads");
    private string TmpRoot => Path.Combine(Root, ".tmp");
    private int MaxMb => _cfg.GetValue<int>("Storage:MaxFileSizeMb", 10);
    private int ChunkSize => Math.Clamp(_cfg.GetValue<int>("Storage:ChunkSizeKb", 1024), 64, 8192) * 1024;
    /// <summary>单用户同时存在的分片会话上限。</summary>
    private int MaxSessionsPerUser => _cfg.GetValue<int>("Storage:MaxSessionsPerUser", 5);
    /// <summary>单个分片会话的最大临时占用(按声明大小的 1.2 倍放宽)。</summary>
    private const double TempWasteFactor = 1.2;

    /// <summary>证件材料等私有文件的子目录。静态服务会拒绝该前缀(见 UploadsFileProvider)。</summary>
    private const string PrivateDir = ".private";
    /// <summary>私有文件签名有效期。</summary>
    private static readonly TimeSpan SignTtl = TimeSpan.FromMinutes(10);


    // ---------- 单文件上传 ----------

    public async Task<string> SaveImageAsync(long userId, IFormFile file, bool isPrivate = false)
    {
        if (file.Length == 0) throw new BizException("文件为空");
        if (file.Length > MaxMb * 1024L * 1024L) throw new BizException($"文件不能超过 {MaxMb}MB");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) throw new BizException("仅支持 jpg/png/gif/webp 图片");

        using var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(ms);
        var clean = ImageSanitizer.Sanitize(ms.ToArray(), ext);
        return await PersistAsync(userId, clean, ext, isPrivate);
    }

    // ---------- 分片上传(断点续传) ----------

    public async Task<UploadInitResult> InitUploadAsync(long userId, string fileName, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new BizException("缺少文件名");
        if (fileSize <= 0) throw new BizException("文件为空");
        if (fileSize > MaxMb * 1024L * 1024L) throw new BizException($"文件不能超过 {MaxMb}MB");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) throw new BizException("仅支持 jpg/png/gif/webp 图片");

        await CleanupStaleAsync();
        await EnforceQuotaAsync(userId);

        var chunkSize = ChunkSize;
        var total = (int)Math.Ceiling((double)fileSize / chunkSize);
        var uploadId = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(TmpRoot, uploadId);
        Directory.CreateDirectory(dir);
        var meta = new UploadMeta(uploadId, userId, fileName, fileSize, chunkSize, total, ext,
            DateTime.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta));
        return new UploadInitResult(uploadId, chunkSize, total, new List<int>());
    }

    public async Task<UploadStatus> GetStatusAsync(long userId, string uploadId)
    {
        var dir = TmpDir(uploadId);
        var meta = await ReadMetaAsync(dir);
        EnsureOwner(meta, userId);
        return new UploadStatus(meta.UploadId, meta.FileName, meta.FileSize, meta.ChunkSize, meta.TotalChunks, ListChunks(dir));
    }

    public async Task<List<int>> SaveChunkAsync(long userId, string uploadId, int index, IFormFile chunk)
    {
        var dir = TmpDir(uploadId);
        var meta = await ReadMetaAsync(dir);
        EnsureOwner(meta, userId);
        if (index < 0 || index >= meta.TotalChunks) throw new BizException("分片序号超出范围");
        if (chunk == null || chunk.Length == 0) throw new BizException("分片内容为空");
        var maxChunk = meta.ChunkSize + 1024 * 1024; // 允许少量冗余, 防止异常超大分片
        if (chunk.Length > maxChunk) throw new BizException("分片大小超出限制");

        // 分片总量也要卡住: 否则可以只 init 一个小文件, 然后往同一会话里无限塞大分片
        var existing = ListChunks(dir).Where(i => i != index)
            .Sum(i => new FileInfo(Path.Combine(dir, $"{i}.part")).Length);
        if (existing + chunk.Length > meta.FileSize * TempWasteFactor)
            throw new BizException("分片总量超出声明大小");

        await using (var fs = File.Create(Path.Combine(dir, $"{index}.part")))
            await chunk.CopyToAsync(fs);
        return ListChunks(dir);
    }

    public async Task<string> CompleteUploadAsync(long userId, string uploadId)
    {
        var dir = TmpDir(uploadId);
        var meta = await ReadMetaAsync(dir);
        EnsureOwner(meta, userId);
        var uploaded = ListChunks(dir);
        if (uploaded.Count != meta.TotalChunks)
            throw new BizException($"分片不完整, 已上传 {uploaded.Count}/{meta.TotalChunks} 片");

        var ext = meta.Ext;
        var sub = DateTime.UtcNow.ToString("yyyyMMdd");
        Directory.CreateDirectory(Path.Combine(Root, sub));
        var name = $"{Guid.NewGuid():N}{ext}";
        var finalPath = Path.Combine(Root, sub, name);
        try
        {
            await using (var outFs = File.Create(finalPath))
            {
                for (var i = 0; i < meta.TotalChunks; i++)
                {
                    await using var part = File.OpenRead(Path.Combine(dir, $"{i}.part"));
                    await part.CopyToAsync(outFs);
                }
            }
            if (new FileInfo(finalPath).Length != meta.FileSize)
                throw new BizException("合并后文件大小与声明不一致");

            // 合并后整体净化: 只读前 16 字节判断魔数时, 第 1 片放 PNG 头、第 2 片放 HTML 就能过关
            var merged = await File.ReadAllBytesAsync(finalPath);
            var clean = ImageSanitizer.Sanitize(merged, ext);
            await File.WriteAllBytesAsync(finalPath, clean);
            await RegisterAsync(userId, $"/uploads/{sub}/{name}", clean.Length);

            Directory.Delete(dir, true);
            return $"/uploads/{sub}/{name}";
        }
        catch
        {
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
    }

    /// <summary>
    /// 按归属删除: 只有登记过、且登记人是调用者本人的文件才允许删。
    /// 没有归属登记时, 要么谁都能删(等于任意删站内图片), 要么谁都删不了。
    /// </summary>
    public async Task DeleteByUrlAsync(long userId, string? url)
    {
        if (!IsOwnUploadUrl(url)) throw new BizException("文件地址无效");
        using var conn = _db.Create();
        var owner = await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT user_id FROM sys_upload WHERE url=@url", new { url });
        if (owner == null || owner != userId)
            throw new BizException("文件不存在", ErrorCodes.NotFound);
        await conn.ExecuteAsync("DELETE FROM sys_upload WHERE url=@url", new { url });
        RemoveFile(url!);
    }

    /// <summary>仅删除磁盘文件(供运维/替换旧图时内部调用), 不校验归属。</summary>
    public void RemoveFile(string? url)
    {
        var full = ResolvePath(url);
        if (full == null) return;
        try { if (File.Exists(full)) File.Delete(full); } catch { /* 忽略 */ }
    }

    /// <summary>
    /// 判断一个前端回传的地址是不是本服务产出的文件地址。
    /// 头像/资质这类字段是前端直接传上来的字符串, 不校验就等于让用户把任意外链(甚至 javascript:)
    /// 写进资料页, 由平台原样渲染给其他用户。
    /// </summary>
    public static bool IsOwnUploadUrl(string? url)
    {
        // 私有证件材料存的是 /api/files/private/... , 必须同样接受, 否则提交认证会被判"地址无效"
        var prefix = IsPrivateUrl(url) ? "/api/files/private/" : "/uploads/";
        if (!url!.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var parts = url[prefix.Length..].Split('/');
        if (parts.Length != 2) return false;
        if (parts[0].Length != 8 || !parts[0].All(char.IsAsciiDigit)) return false;
        if (!AllowedExt.Contains(Path.GetExtension(parts[1]).ToLowerInvariant())) return false;
        return Path.GetFileNameWithoutExtension(parts[1]).Length == 32;
    }

    // ---------- 私有文件: 签名地址 ----------

    /// <summary>
    /// 把私有文件地址变成带签名的临时地址。
    /// 用签名而不是"要求登录头"的原因: 小程序与 <image> 组件都没法给图片请求带 Authorization,
    /// 强鉴权会逼前端改成 fetch+blob; 签名放在 query 里则前端零改动, 而地址本身不可伪造、10 分钟过期。
    /// </summary>
    public string SignPrivateUrl(string url)
    {
        if (!IsPrivateUrl(url)) return url;
        var exp = DateTimeOffset.UtcNow.Add(SignTtl).ToUnixTimeSeconds();
        return $"{url}?e={exp}&s={Sign(url, exp)}";
    }

    /// <summary>校验签名与有效期。</summary>
    public bool VerifyPrivateUrl(string url, string? expText, string? sig)
    {
        if (!IsPrivateUrl(url) || !long.TryParse(expText, out var exp) || string.IsNullOrEmpty(sig)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;
        var expected = Sign(url, exp);
        // 定长时间比较, 避免按字节差异逐步试出签名
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(sig.ToLowerInvariant()));
    }

    private string Sign(string url, long exp)
    {
        var key = _cfg["Storage:SignKey"] ?? _cfg["Jwt:SecretKey"]
                  ?? throw new BizException("存储签名密钥未配置");
        var mac = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key),
            System.Text.Encoding.UTF8.GetBytes($"{url}|{exp}"));
        return Convert.ToHexString(mac).ToLowerInvariant()[..32];
    }

    /// <summary>是否是本服务签发的私有文件地址。</summary>
    public static bool IsPrivateUrl(string? url)
        => !string.IsNullOrEmpty(url) && url.StartsWith("/api/files/private/", StringComparison.Ordinal);

    /// <summary>把 /api/files/private/yyyyMMdd/name.ext 解析为磁盘路径; 非法返回 null。</summary>
    public string? ResolvePrivatePath(string? url)
    {
        if (!IsPrivateUrl(url)) return null;
        var rel = url!["/api/files/private/".Length..];
        var parts = rel.Split('/');
        if (parts.Length != 2) return null;
        if (parts[0].Length != 8 || !parts[0].All(char.IsAsciiDigit)) return null;
        if (!AllowedExt.Contains(Path.GetExtension(parts[1]).ToLowerInvariant())) return null;
        if (Path.GetFileNameWithoutExtension(parts[1]).Length != 32) return null;
        var full = Path.GetFullPath(Path.Combine(Root, PrivateDir, parts[0], parts[1]));
        var baseDir = Path.GetFullPath(Path.Combine(Root, PrivateDir));
        return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>私有文件的 Content-Type(只按白名单回图片类型)。</summary>
    public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private string? ResolvePath(string? url)
    {
        if (!IsOwnUploadUrl(url)) return null;
        var parts = url!["/uploads/".Length..].Split('/');
        var full = Path.GetFullPath(Path.Combine(Root, parts[0], parts[1]));
        var rootFull = Path.GetFullPath(Root);
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    // ---------- 内部工具 ----------

    private async Task<string> PersistAsync(long userId, byte[] bytes, string ext, bool isPrivate)
    {
        var sub = DateTime.UtcNow.ToString("yyyyMMdd");
        var dir = isPrivate ? Path.Combine(Root, PrivateDir, sub) : Path.Combine(Root, sub);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes);

        // 私有文件对外只有一个"凭签名取"的地址, 不再有可直接 GET 的静态路径
        var url = isPrivate ? $"/api/files/private/{sub}/{name}" : $"/uploads/{sub}/{name}";
        await RegisterAsync(userId, url, bytes.Length, isPrivate);
        return url;
    }

    /// <summary>登记归属, 供删除授权与后续清理使用。失败不影响上传本身。</summary>
    private async Task RegisterAsync(long userId, string url, long size, bool isPrivate = false)
    {
        try
        {
            using var conn = _db.Create();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO sys_upload (user_id, url, size_bytes, is_private) VALUES (@u, @url, @s, @p)",
                new { u = userId, url, s = size, p = isPrivate ? 1 : 0 });
        }
        catch { /* 登记失败不阻断上传; 只是该文件后续无法被用户自助删除 */ }
    }

    private static void EnsureOwner(UploadMeta meta, long userId)
    {
        // 上传会话只属于发起人: 否则拿别人的 uploadId 就能塞入任意内容并让对方的完成请求落盘
        if (meta.UserId != userId)
            throw new BizException("上传会话不存在或已过期", ErrorCodes.NotFound);
    }

    /// <summary>限制单用户的并发会话数与临时目录总占用, 防止初始化大量会话把磁盘填满。</summary>
    private async Task EnforceQuotaAsync(long userId)
    {
        if (!Directory.Exists(TmpRoot)) return;
        var mine = new List<(string Dir, UploadMeta Meta)>();
        foreach (var d in Directory.EnumerateDirectories(TmpRoot))
        {
            try
            {
                var meta = await ReadMetaAsync(d);
                if (meta.UserId == userId) mine.Add((d, meta));
            }
            catch { /* 损坏或正在清理的会话忽略 */ }
        }

        if (mine.Count >= MaxSessionsPerUser)
        {
            // 把最老的会话清掉而不是直接拒绝, 避免用户中断后无法重新上传
            foreach (var old in mine.OrderBy(m => m.Meta.CreatedAtUtc).Take(mine.Count - MaxSessionsPerUser + 1))
            {
                try { Directory.Delete(old.Dir, true); } catch { }
            }
        }
    }

    private string TmpDir(string uploadId)
    {
        if (!Guid.TryParseExact(uploadId, "N", out _)) throw new BizException("上传标识无效");
        var dir = Path.Combine(TmpRoot, uploadId);
        if (!Directory.Exists(dir)) throw new BizException("上传会话不存在或已过期", ErrorCodes.NotFound);
        return dir;
    }

    private static List<int> ListChunks(string dir) =>
        Directory.EnumerateFiles(dir, "*.part")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(s => int.TryParse(s, out var i) ? i : -1)
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();

    private static async Task<UploadMeta> ReadMetaAsync(string dir)
    {
        var path = Path.Combine(dir, "meta.json");
        if (!File.Exists(path)) throw new BizException("上传会话不存在或已过期", ErrorCodes.NotFound);
        var meta = JsonSerializer.Deserialize<UploadMeta>(await File.ReadAllTextAsync(path));
        if (meta == null) throw new BizException("上传会话损坏");
        return meta;
    }

    private async Task CleanupStaleAsync()
    {
        if (!Directory.Exists(TmpRoot)) return;
        var now = DateTime.UtcNow;
        foreach (var d in Directory.EnumerateDirectories(TmpRoot))
        {
            try
            {
                if (now - Directory.GetCreationTimeUtc(d) > TmpTtl) Directory.Delete(d, true);
            }
            catch { /* 忽略清理失败 */ }
        }
        await Task.CompletedTask;
    }

    private sealed record UploadMeta(string UploadId, long UserId, string FileName, long FileSize,
        int ChunkSize, int TotalChunks, string Ext, DateTime CreatedAtUtc);

    /// <summary>兼容旧调用方: 仅做格式判定。真正的校验与净化走 ImageSanitizer。</summary>
    public static bool IsImage(byte[] b) => ImageSanitizer.IsPlausibleImage(b);
}
