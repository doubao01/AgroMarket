using AgroMarket.Api.Common;
using AgroMarket.Api.Data;
using Dapper;

namespace AgroMarket.Api.Notifications;

/// <summary>
/// 留存期限设置 (单例): 管理员可在后台动态调整, 无需改配置重启。
/// 覆盖优先级: retention_setting(本表) &gt; 代码默认值(Defaults)。
/// 与 FederationSettings 同款设计: 只存"覆盖值", 未写入的表项回落默认。
/// </summary>
public class RetentionSettings
{
    /// <summary>允许后台修改的键。</summary>
    public static readonly string[] Keys =
    {
        "Enabled", "ChatMessageDays", "NotificationDays", "OperationLogDays",
        "FederationDays", "SmsCodeDays",
    };

    /// <summary>代码默认值 (业务/合规拍板, 后台可覆盖)。</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>
        {
            ["Enabled"] = "false",
            ["ChatMessageDays"] = "365",
            ["NotificationDays"] = "180",
            ["OperationLogDays"] = "180",
            ["FederationDays"] = "365",
            ["SmsCodeDays"] = "7",
        };

    private readonly IDbConnectionFactory _db;
    private readonly object _gate = new();
    private Dictionary<string, string?>? _cache;

    public RetentionSettings(IDbConnectionFactory db) => _db = db;

    /// <summary>留存清理开关。</summary>
    public bool Enabled => GetInt("Enabled") != 0;

    public int ChatMessageDays => GetInt("ChatMessageDays");
    public int NotificationDays => GetInt("NotificationDays");
    public int OperationLogDays => GetInt("OperationLogDays");
    public int FederationDays => GetInt("FederationDays");
    public int SmsCodeDays => GetInt("SmsCodeDays");

    /// <summary>后台读全部生效值。</summary>
    public Dictionary<string, string> Snapshot()
    {
        var raw = Load();
        var result = new Dictionary<string, string>();
        foreach (var k in Keys)
            result[k] = raw.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : Defaults[k];
        return result;
    }

    /// <summary>后台批量写入覆盖 (null/空串 = 清除覆盖, 回退默认)。</summary>
    public void Update(IReadOnlyDictionary<string, string?> values)
    {
        var toWrite = new List<(string Key, string? Value)>();
        foreach (var (k, v) in values)
        {
            if (!Keys.Contains(k, StringComparer.OrdinalIgnoreCase))
                throw new BizException($"不支持后台修改的设置项: {k}", ErrorCodes.BadRequest);
            var key = Keys.First(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
            ValidateValue(key, v);
            toWrite.Add((key, string.IsNullOrWhiteSpace(v) ? null : v!.Trim()));
        }
        using var conn = _db.Create();
        foreach (var (key, value) in toWrite)
        {
            if (value is null)
                conn.Execute("DELETE FROM retention_setting WHERE setting_key=@key", new { key });
            else
                conn.Execute(
                    @"INSERT INTO retention_setting (setting_key, setting_value) VALUES (@key, @value)
                      ON DUPLICATE KEY UPDATE setting_value=VALUES(setting_value)",
                    new { key, value });
        }
        lock (_gate) _cache = null;
    }

    private int GetInt(string key)
    {
        var raw = Load();
        if (raw.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            && int.TryParse(v, out var n))
            return n;
        return int.TryParse(Defaults[key], out var d) ? d : 0;
    }

    private static void ValidateValue(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var v = value.Trim();
        switch (key)
        {
            case "Enabled" when v is not ("0" or "1"):
                throw new BizException("Enabled 需为 0 或 1", ErrorCodes.BadRequest);
            case var _ when key != "Enabled" && (!int.TryParse(v, out var n) || n < 0 || n > 3650):
                throw new BizException($"{key} 需为 0..3650 的整数 (0=该类数据不清理)", ErrorCodes.BadRequest);
        }
    }

    private Dictionary<string, string?> Load()
    {
        lock (_gate)
        {
            if (_cache is not null) return _cache;
            using var conn = _db.Create();
            var rows = conn.Query<(string Key, string? Value)>(
                "SELECT setting_key AS `Key`, setting_value AS `Value` FROM retention_setting");
            _cache = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
            return _cache;
        }
    }
}
