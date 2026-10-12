using System.Text.Json;
using AgroMarket.Api.Common;

namespace AgroMarket.Api.Services;

/// <summary>本地上传结果: 分片上传初始化信息。</summary>
public record UploadInitResult(string UploadId, int ChunkSize, int TotalChunks, List<int> UploadedChunks);

/// <summary>分片上传状态。</summary>
public record UploadStatus(string UploadId, string FileName, long FileSize, int ChunkSize, int TotalChunks, List<int> UploadedChunks);

/// <summary>
/// 本地文件存储, 支持单文件与分片(断点续传)上传; 预留 S3 兼容接口。
/// 仅允许图片类型, 限制大小, 魔数校验防伪造扩展名。
/// </summary>
public class StorageService
{
    private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private static readonly TimeSpan TmpTtl = TimeSpan.FromHours(24);
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;

    public StorageService(IConfiguration cfg, IWebHostEnvironment env) { _cfg = cfg; _env = env; }

    private string Root => Path.Combine(_env.ContentRootPath, _cfg["Storage:LocalRoot"] ?? "uploads");
    private string TmpRoot => Path.Combine(Root, ".tmp");
    private int MaxMb => _cfg.GetValue<int>("Storage:MaxFileSizeMb", 10);
    private int ChunkSize => Math.Clamp(_cfg.GetValue<int>("Storage:ChunkSizeKb", 1024), 64, 8192) * 1024;

    // ---------- 单文件上传 ----------

    public async Task<string> SaveImageAsync(IFormFile file)
    {
        if (file.Length == 0) throw new BizException("文件为空");
        if (file.Length > MaxMb * 1024L * 1024L) throw new BizException($"文件不能超过 {MaxMb}MB");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) throw new BizException("仅支持 jpg/png/gif/webp 图片");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (!IsImage(bytes)) throw new BizException("文件内容不是有效图片");
        return await PersistAsync(bytes, ext);
    }

    // ---------- 分片上传(断点续传) ----------

    public async Task<UploadInitResult> InitUploadAsync(string fileName, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new BizException("缺少文件名");
        if (fileSize <= 0) throw new BizException("文件为空");
        if (fileSize > MaxMb * 1024L * 1024L) throw new BizException($"文件不能超过 {MaxMb}MB");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) throw new BizException("仅支持 jpg/png/gif/webp 图片");

        await CleanupStaleAsync();

        var chunkSize = ChunkSize;
        var total = (int)Math.Ceiling((double)fileSize / chunkSize);
        var uploadId = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(TmpRoot, uploadId);
        Directory.CreateDirectory(dir);
        var meta = new UploadMeta(uploadId, fileName, fileSize, chunkSize, total, ext);
        await File.WriteAllTextAsync(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta));
        return new UploadInitResult(uploadId, chunkSize, total, new List<int>());
    }

    public async Task<UploadStatus> GetStatusAsync(string uploadId)
    {
        var dir = TmpDir(uploadId);
        var meta = await ReadMetaAsync(dir);
        return new UploadStatus(meta.UploadId, meta.FileName, meta.FileSize, meta.ChunkSize, meta.TotalChunks, ListChunks(dir));
    }

    public async Task<List<int>> SaveChunkAsync(string uploadId, int index, IFormFile chunk)
    {
        var dir = TmpDir(uploadId);
        var meta = await ReadMetaAsync(dir);
        if (index < 0 || index >= meta.TotalChunks) throw new BizException("分片序号超出范围");
        if (chunk == null || chunk.Length == 0) throw new BizException("分片内容为空");
        var maxChunk = meta.ChunkSize + 1024 * 1024; // 允许少量冗余, 防止异常超大分片
        if (chunk.Length > maxChunk) throw new BizException("分片大小超出限制");
        await using (var fs = File.Create(Path.Combine(dir, $"{index}.part")))
            await chunk.CopyToAsync(fs);
        return ListChunks(dir);
    }

    public async Task<string> CompleteUploadAsync(string uploadId)
    {
        var dir = TmpDir(uploadId);
        var meta = await ReadMetaAsync(dir);
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
            var head = new byte[16];
            await using (var fs = File.OpenRead(finalPath))
            {
                var n = await fs.ReadAsync(head.AsMemory(0, head.Length));
                if (!IsImage(head.AsSpan(0, n).ToArray())) throw new BizException("文件内容不是有效图片");
            }
            if (new FileInfo(finalPath).Length != meta.FileSize) throw new BizException("合并后文件大小与声明不一致");
            Directory.Delete(dir, true);
            return $"/uploads/{sub}/{name}";
        }
        catch
        {
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
    }

    // ---------- 内部工具 ----------

    private async Task<string> PersistAsync(byte[] bytes, string ext)
    {
        var sub = DateTime.UtcNow.ToString("yyyyMMdd");
        Directory.CreateDirectory(Path.Combine(Root, sub));
        var name = $"{Guid.NewGuid():N}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Root, sub, name), bytes);
        return $"/uploads/{sub}/{name}";
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

    private sealed record UploadMeta(string UploadId, string FileName, long FileSize, int ChunkSize, int TotalChunks, string Ext);

    public static bool IsImage(byte[] b) =>
        (b.Length >= 3 && b.AsSpan(0, 3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) ||
        (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) ||
        (b.Length >= 6 && (b.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || b.AsSpan(0, 6).SequenceEqual("GIF89a"u8))) ||
        (b.Length >= 12 && b.AsSpan(0, 4).SequenceEqual("RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WEBP"u8));
}
