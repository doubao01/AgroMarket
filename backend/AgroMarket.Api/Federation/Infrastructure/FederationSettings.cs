using AgroMarket.Api.Common;
using AgroMarket.Api.Data;
using Dapper;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Infrastructure;

/// <summary>
/// 联动运行时设置 (单例): 允许管理员在界面动态覆盖"可安全热改"的参数。
/// 覆盖优先级: fed_setting(本表) &gt; appsettings/环境变量。
/// 身份与密钥类字段 (Enabled/NodeCode/Level/TokenMode/SharedKey/Role) 不在此列, 需重启生效。
/// </summary>
public class FederationSettings
{
    /// <summary>允许运行时覆盖的键 (白名单, 防止写入任意项)。</summary>
    public static readonly string[] Keys =
    {
        "AutoSync", "SyncIntervalMinutes", "BackfillDays", "RecordWindowHours",
        "IncludeSelfInAggregate", "ForwardReceived", "AcceptForwarded", "HealthStaleMinutes",
        "NodeName", "RegionCode",
    };

    private readonly IDbConnectionFactory _db;
    private readonly FederationOptions _opt;
    private readonly object _gate = new();
    private Dictionary<string, string?>? _cache;

    public FederationSettings(IDbConnectionFactory db, IOptions<FederationOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    // ---------------- 生效值 (覆盖优先) ----------------

    public bool AutoSync => GetBool("AutoSync", _opt.AutoSync);
    public int SyncIntervalMinutes => GetInt("SyncIntervalMinutes", _opt.SyncIntervalMinutes);
    public int BackfillDays => GetInt("BackfillDays", _opt.BackfillDays);
    public int RecordWindowHours => GetInt("RecordWindowHours", _opt.RecordWindowHours);
    public bool IncludeSelfInAggregate => GetBool("IncludeSelfInAggregate", _opt.IncludeSelfInAggregate);
    public bool ForwardReceived => GetBool("ForwardReceived", _opt.ForwardReceived);
    public bool AcceptForwarded => GetBool("AcceptForwarded", _opt.AcceptForwarded);
    public int HealthStaleMinutes => GetInt("HealthStaleMinutes", _opt.HealthStaleMinutes);
    public string NodeName => Get("NodeName") ?? _opt.NodeName;
    public string? RegionCode => Get("RegionCode") ?? _opt.RegionCode;

    /// <summary>后台同步是否应运行 (结合只读/身份可由配置决定)。</summary>
    public bool IsSyncActive => _opt.Enabled && AutoSync && _opt.HasIdentity;

    public int EffectiveStaleMinutes => HealthStaleMinutes > 0
        ? HealthStaleMinutes
        : Math.Max(SyncIntervalMinutes * 2, 60);

    // ---------------- 读写 ----------------

    /// <summary>返回全部白名单键的生效值 (含来源: 已覆盖/默认)。</summary>
    public Dictionary<string, string?> Snapshot()
    {
        var raw = Load();
        var result = new Dictionary<string, string?>();
        foreach (var k in Keys)
            result[k] = raw.TryGetValue(k, out var v) ? v : DefaultString(k);
        return result;
    }

    /// <summary>批量写入覆盖 (null/空串 表示清除该覆盖, 回退默认)。</summary>
    public void Update(IReadOnlyDictionary<string, string?> values)
    {
        var toWrite = new List<(string Key, string? Value)>();
        foreach (var (k, v) in values)
        {
            if (!Keys.Contains(k, StringComparer.OrdinalIgnoreCase))
                throw new BizException($"不支持运行时修改的设置项: {k}", ErrorCodes.BadRequest);
            var key = Keys.First(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
            ValidateValue(key, v);
            toWrite.Add((key, string.IsNullOrWhiteSpace(v) ? null : v!.Trim()));
        }
        using var conn = _db.Create();
        foreach (var (key, value) in toWrite)
        {
            if (value is null)
                conn.Execute("DELETE FROM fed_setting WHERE setting_key=@key", new { key });
            else
                conn.Execute(
                    @"INSERT INTO fed_setting (setting_key, setting_value) VALUES (@key, @value)
                      ON DUPLICATE KEY UPDATE setting_value=VALUES(setting_value)",
                    new { key, value });
        }
        lock (_gate) _cache = null;   // 失效缓存
    }

    // ---------------- 内部 ----------------

    private string? Get(string key)
    {
        var raw = Load();
        return raw.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    private bool GetBool(string key, bool fallback)
    {
        var v = Get(key);
        return v is null ? fallback : (v is "1" or "true" or "TRUE" or "True");
    }

    private int GetInt(string key, int fallback)
    {
        var v = Get(key);
        return v is not null && int.TryParse(v, out var n) ? n : fallback;
    }

    private string? DefaultString(string key) => key switch
    {
        "AutoSync" => _opt.AutoSync.ToString().ToLowerInvariant(),
        "SyncIntervalMinutes" => _opt.SyncIntervalMinutes.ToString(),
        "BackfillDays" => _opt.BackfillDays.ToString(),
        "RecordWindowHours" => _opt.RecordWindowHours.ToString(),
        "IncludeSelfInAggregate" => _opt.IncludeSelfInAggregate.ToString().ToLowerInvariant(),
        "ForwardReceived" => _opt.ForwardReceived.ToString().ToLowerInvariant(),
        "AcceptForwarded" => _opt.AcceptForwarded.ToString().ToLowerInvariant(),
        "HealthStaleMinutes" => _opt.HealthStaleMinutes.ToString(),
        "NodeName" => _opt.NodeName,
        "RegionCode" => _opt.RegionCode,
        _ => null,
    };

    private static void ValidateValue(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var v = value.Trim();
        switch (key)
        {
            case "SyncIntervalMinutes" when !int.TryParse(v, out var m) || m < 1 || m > 1440:
                throw new BizException("SyncIntervalMinutes 需为 1..1440 的整数", ErrorCodes.BadRequest);
            case "BackfillDays" when !int.TryParse(v, out var d) || d < 1 || d > 90:
                throw new BizException("BackfillDays 需为 1..90 的整数", ErrorCodes.BadRequest);
            case "RecordWindowHours" when !int.TryParse(v, out var h) || h < 1 || h > 720:
                throw new BizException("RecordWindowHours 需为 1..720 的整数", ErrorCodes.BadRequest);
            case "HealthStaleMinutes" when !int.TryParse(v, out var s) || s < 0 || s > 10080:
                throw new BizException("HealthStaleMinutes 需为 0..10080 的整数", ErrorCodes.BadRequest);
            case "AutoSync" or "IncludeSelfInAggregate" or "ForwardReceived" or "AcceptForwarded"
                when v is not ("true" or "false" or "1" or "0" or "TRUE" or "FALSE" or "True" or "False"):
                throw new BizException($"{key} 需为布尔值", ErrorCodes.BadRequest);
            case "NodeName" when v.Length > 128:
                throw new BizException("NodeName 长度需 <= 128", ErrorCodes.BadRequest);
            case "RegionCode" when v.Length > 20:
                throw new BizException("RegionCode 长度需 <= 20", ErrorCodes.BadRequest);
        }
    }

    private Dictionary<string, string?> Load()
    {
        lock (_gate)
        {
            if (_cache is not null) return _cache;
            using var conn = _db.Create();
            var rows = conn.Query<(string Key, string? Value)>(
                "SELECT setting_key AS `Key`, setting_value AS `Value` FROM fed_setting");
            _cache = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
            return _cache;
        }
    }
}
