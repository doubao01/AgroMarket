using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class AddressService
{
    private readonly IDbConnectionFactory _db;
    private readonly RegionService _regions;
    public AddressService(IDbConnectionFactory db, RegionService regions) { _db = db; _regions = regions; }

    public async Task<List<Dtos.AddressDto>> QueryAsync(long userId)
    {
        using var conn = _db.Create();
        var rows = (await conn.QueryAsync<(long Id, string ContactName, string Phone, long? CountyId, long? TownId, long? VillageId, string Detail, bool IsDefault)>(
            @"SELECT id, contact_name, phone, county_id, town_id, village_id, detail, is_default
              FROM trade_address WHERE user_id=@u ORDER BY is_default DESC, id DESC", new { u = userId })).ToList();
        var result = new List<Dtos.AddressDto>();
        foreach (var r in rows)
        {
            // 根据区县反查省/市 (原实现硬编码 410000 河南省/410100 郑州市)。
            var (province, city, county) = await _regions.GetAncestryAsync(r.CountyId);
            var (_, tName, vName) = await _regions.GetRegionNamesAsync(r.CountyId, r.TownId, r.VillageId);
            var countyName = r.CountyId == null ? null : (await _regions.GetRegionNamesAsync(r.CountyId, null, null)).Item1;
            result.Add(new Dtos.AddressDto(r.Id, r.ContactName, r.Phone, province?.Id, city?.Id, r.CountyId, r.TownId, r.VillageId,
                $"{countyName ?? ""}{tName ?? ""}{vName ?? ""} {r.Detail}", r.IsDefault));
        }
        return result;
    }

    public async Task<long> CreateAsync(long userId, Dtos.AddressRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ContactName)) throw new BizException("请填写联系人");
        if (string.IsNullOrWhiteSpace(req.Phone)) throw new BizException("请填写联系电话");
        if (string.IsNullOrWhiteSpace(req.Detail)) throw new BizException("请填写详细地址");
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            if (req.IsDefault)
                await conn.ExecuteAsync("UPDATE trade_address SET is_default=0 WHERE user_id=@u", new { u = userId }, tx);
            var id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO trade_address (user_id, contact_name, phone, county_id, town_id, village_id, detail, is_default)
                VALUES (@u, @cn, @ph, @county, @town, @village, @d, @def); SELECT LAST_INSERT_ID();",
                new { u = userId, cn = req.ContactName, ph = req.Phone, county = req.CountyId, town = req.TownId,
                    village = req.VillageId, d = req.Detail, def = req.IsDefault }, tx);
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task DeleteAsync(long userId, long id)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync("DELETE FROM trade_address WHERE id=@id AND user_id=@u", new { id, u = userId });
    }
}
