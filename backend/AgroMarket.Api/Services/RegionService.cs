using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class RegionService
{
    private readonly Data.IDbConnectionFactory _db;
    public RegionService(Data.IDbConnectionFactory db) => _db = db;

    public async Task<List<Dtos.RegionDto>> GetChildrenAsync(long? parentId)
    {
        using var conn = _db.Create();
        var sql = parentId is null
            ? "SELECT id, region_code Code, region_name Name, region_level Level, parent_id ParentId FROM sys_region WHERE parent_id IS NULL AND is_enabled=1 ORDER BY sort_order, id"
            : "SELECT id, region_code Code, region_name Name, region_level Level, parent_id ParentId FROM sys_region WHERE parent_id=@pid AND is_enabled=1 ORDER BY sort_order, id";
        return (await conn.QueryAsync<Dtos.RegionDto>(sql, new { pid = parentId })).ToList();
    }

    public async Task<List<Dtos.RegionNode>> GetTreeAsync(string? rootCode)
    {
        using var conn = _db.Create();
        long? rootId = null;
        if (!string.IsNullOrEmpty(rootCode))
        {
            rootId = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM sys_region WHERE region_code=@c", new { c = rootCode });
            if (rootId == null) throw new BizException("根区域编码不存在", ErrorCodes.NotFound);
        }
        var all = (await conn.QueryAsync<Dtos.RegionDto>(
            "SELECT id, region_code Code, region_name Name, region_level Level, parent_id ParentId FROM sys_region WHERE is_enabled=1 ORDER BY sort_order, id")).ToList();
        var lookup = all.ToLookup(r => r.ParentId);
        List<Dtos.RegionNode> Build(long? parent) => lookup[parent]
            .Select(r => new Dtos.RegionNode(r.Id, r.Code, r.Name, r.Level, Build(r.Id))).ToList();
        var roots = Build(rootId);
        // 指定根节点时, 将根自身作为树根返回
        if (rootId != null)
        {
            var root = all.First(r => r.Id == rootId);
            return new List<Dtos.RegionNode> { new(root.Id, root.Code, root.Name, root.Level, roots) };
        }
        return roots;
    }

    /// <summary>校验区域存在且启用, 返回区域名。区域链校验: child 必须是 parent 的后代或同级。</summary>
    public async Task<(long Id, string Name)> ValidateRegionAsync(long? regionId, long? expectedParentId, int? expectedLevel = null)
    {
        if (regionId == null) return (0, "");
        using var conn = _db.Create();
        var region = await conn.QueryFirstOrDefaultAsync<(long Id, string Name, long? ParentId, int Level, int Enabled)>(
            "SELECT id, region_name, parent_id, region_level, is_enabled FROM sys_region WHERE id=@id", new { id = regionId });
        if (region.Id == 0) throw new BizException("区域不存在", ErrorCodes.NotFound);
        if (region.Enabled != 1) throw new BizException($"区域 [{region.Name}] 已停用");
        if (expectedLevel.HasValue && region.Level != expectedLevel.Value)
            throw new BizException($"区域 [{region.Name}] 层级不正确");
        if (expectedParentId != null && region.ParentId != expectedParentId)
            throw new BizException($"区域 [{region.Name}] 与上级区域不匹配");
        return (region.Id, region.Name);
    }

    public async Task<(string? CountyName, string? TownName, string? VillageName)> GetRegionNamesAsync(long? countyId, long? townId, long? villageId)
    {
        using var conn = _db.Create();
        const string sql = "SELECT region_name FROM sys_region WHERE id=@id";
        return (
            countyId == null ? null : await conn.ExecuteScalarAsync<string?>(sql, new { id = countyId }),
            townId == null ? null : await conn.ExecuteScalarAsync<string?>(sql, new { id = townId }),
            villageId == null ? null : await conn.ExecuteScalarAsync<string?>(sql, new { id = villageId }));
    }

    /// <summary>沿 parent_id 向上回溯, 返回 [省, 市, 区县, 乡镇, 村] 中命中的 (Id, Name, Level), 缺失层级为 null。</summary>
    public async Task<(Dtos.RegionDto? Province, Dtos.RegionDto? City, Dtos.RegionDto? County)> GetAncestryAsync(long? regionId)
    {
        if (regionId == null) return (null, null, null);
        using var conn = _db.Create();
        var chain = new List<Dtos.RegionDto>();
        var cur = regionId;
        // 最多回溯 8 层, 防止数据环导致死循环
        for (var i = 0; i < 8 && cur != null; i++)
        {
            var r = await conn.QueryFirstOrDefaultAsync<Dtos.RegionDto>(
                "SELECT id Id, region_code Code, region_name Name, region_level Level, parent_id ParentId FROM sys_region WHERE id=@id",
                new { id = cur });
            if (r == null) break;
            chain.Add(r);
            cur = r.ParentId;
        }
        return (
            chain.FirstOrDefault(r => r.Level == 1),
            chain.FirstOrDefault(r => r.Level == 2),
            chain.FirstOrDefault(r => r.Level == 3));
    }
}
