using Microsoft.AspNetCore.StaticFiles;

namespace AgroMarket.Api.Common;

/// <summary>
/// 上传目录专用的 Content-Type 映射。
///
/// 默认的 FileExtensionContentTypeProvider 认得两百多种扩展名, 而上传目录里只会出现
/// 图片。这里收窄成"白名单之外一律 null"(UseStaticFiles 对 null 直接 404), 避免哪天
/// 有 .html/.svg/.js 被写进 uploads 就被当作可执行内容返回。
/// 注意 SVG 不在白名单里: 它自带脚本能力, 不能按"图片"对待。
/// </summary>
public class UploadContentTypeProvider : IContentTypeProvider
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    public bool TryGetContentType(string subpath, out string contentType)
    {
        var ext = Path.GetExtension(subpath);
        if (Map.TryGetValue(ext, out var ct))
        {
            contentType = ct;
            return true;
        }
        contentType = "";
        return false;
    }
}
