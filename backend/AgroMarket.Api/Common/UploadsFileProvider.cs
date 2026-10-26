using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace AgroMarket.Api.Common;

/// <summary>
/// 上传目录的静态文件访问控制。
///
/// uploads 下有两类不该被静态服务直接吐出的内容：
///  - <c>.tmp/</c>   分片上传的临时会话(meta.json 与 .part 分片)；
///  - <c>.private/</c> 商家证件材料，只能凭签名地址经 /api/files/private 取。
/// 物理文件服务默认会照常提供以点开头的目录, 所以这里在 FileProvider 层直接判不存在 ——
/// 靠 Content-Type 白名单只能挡住扩展名, 挡不住"路径本身可枚举"。
/// </summary>
public class UploadsFileProvider : IFileProvider
{
    private static readonly string[] BlockedDirs = { ".tmp", ".private" };
    private readonly PhysicalFileProvider _inner;

    public UploadsFileProvider(string root) => _inner = new PhysicalFileProvider(root);

    private static bool IsBlocked(string subpath)
    {
        var normalized = subpath.Replace('\\', '/');
        foreach (var d in BlockedDirs)
        {
            if (normalized.StartsWith(d + "/", StringComparison.Ordinal) || normalized == d) return true;
            if (normalized.Contains("/" + d + "/", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public IFileInfo GetFileInfo(string subpath)
        => IsBlocked(subpath) ? new NotFoundFileInfo(subpath) : _inner.GetFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath)
        => IsBlocked(subpath) ? NotFoundDirectoryContents.Singleton : _inner.GetDirectoryContents(subpath);

    public IChangeToken Watch(string filter) => _inner.Watch(filter);
}
