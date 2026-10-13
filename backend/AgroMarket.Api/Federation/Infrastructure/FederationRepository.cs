using System.Data;
using AgroMarket.Api.Data;
using Dapper;

namespace AgroMarket.Api.Federation.Infrastructure;

// ---------------- Dapper 行映射类型 ----------------

public class FedNodeRow
{
    public long Id { get; set; }
    public string NodeCode { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string Level { get; set; } = "";
    public string? RegionCode { get; set; }
    public string BaseUrl { get; set; } = "";
    public string? NodeToken { get; set; }
    public string Direction { get; set; } = "";
    public string Status { get; set; } = "";
    public bool HasPublicKey { get; set; }
    public DateTime? LastPushAt { get; set; }
    public DateTime? LastPullAt { get; set; }
    public string? LastError { get; set; }
}

public class FedMetricRow
{
    public string SourceNodeCode { get; set; } = "";
    public string StatDate { get; set; } = "";
    public long UserCount { get; set; }
    public long ActiveUserCount { get; set; }
    public long NewUserCount { get; set; }
    public long SupplyCount { get; set; }
    public long DemandCount { get; set; }
    public long OrderCount { get; set; }
    public decimal TradeAmount { get; set; }
    public long MessageCount { get; set; }
    public long ConversationCount { get; set; }
}

public class FedRecordRow
{
    public long Id { get; set; }
    public string SourceNodeCode { get; set; } = "";
    public string Type { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? RegionCode { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Data { get; set; } = "{}";
    public DateTime ReceivedAt { get; set; }
}

public class FedReportRow
{
    public long Id { get; set; }
    public string SourceNodeCode { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public string PeriodDate { get; set; } = "";
    public int MetricCount { get; set; }
    public int RecordCount { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class FedNodeStatRow
{
    public string SourceNodeCode { get; set; } = "";
    public long OrderCount { get; set; }
    public decimal TradeAmount { get; set; }
}

public class FedAggPointRow
{
    public string StatDate { get; set; } = "";
    public long OrderCount { get; set; }
    public decimal TradeAmount { get; set; }
    public long UserCount { get; set; }
    public long SupplyCount { get; set; }
    public long DemandCount { get; set; }
}

/// <summary>节点联动数据访问 (Dapper)。</summary>
public class FederationRepository
{
    private readonly IDbConnectionFactory _db;
    public FederationRepository(IDbConnectionFactory db) => _db = db;

    private const string NodeSelect = @"
SELECT id AS Id, node_code AS NodeCode, node_name AS NodeName, node_level AS Level,
       region_code AS RegionCode, base_url AS BaseUrl, node_token AS NodeToken,
       direction AS Direction, status AS Status,
       (public_key IS NOT NULL AND public_key <> '') AS HasPublicKey,
       last_push_at AS LastPushAt, last_pull_at AS LastPullAt, last_error AS LastError
FROM fed_node";

    // ---------------- 节点注册表 ----------------

    public async Task<List<FedNodeRow>> ListNodesAsync()
    {
        using var conn = _db.Create();
        return (await conn.QueryAsync<FedNodeRow>($"{NodeSelect} ORDER BY id")).ToList();
    }

    public async Task<FedNodeRow?> GetNodeByIdAsync(long id)
    {
        using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<FedNodeRow>(
            $"{NodeSelect} WHERE id=@id", new { id });
    }

    /// <summary>新增或更新对端节点 (按 node_code 幂等)。</summary>
    public async Task<long> UpsertNodeAsync(string nodeCode, string nodeName, string level, string? regionCode,
        string baseUrl, string? nodeToken, string direction, string status)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO fed_node (node_code, node_name, node_level, region_code, base_url, node_token, direction, status)
              VALUES (@nodeCode, @nodeName, @level, @regionCode, @baseUrl, @nodeToken, @direction, @status)
              ON DUPLICATE KEY UPDATE node_name=VALUES(node_name), node_level=VALUES(node_level),
                  region_code=VALUES(region_code), base_url=VALUES(base_url),
                  node_token=COALESCE(VALUES(node_token), node_token),
                  direction=VALUES(direction), status=VALUES(status), id=LAST_INSERT_ID(id);
              SELECT LAST_INSERT_ID();",
            new { nodeCode, nodeName, level, regionCode, baseUrl, nodeToken, direction, status });
    }

    public async Task<int> UpdateNodeAsync(long id, string nodeName, string level, string? regionCode,
        string baseUrl, string? nodeToken, string direction, string status)
    {
        using var conn = _db.Create();
        return await conn.ExecuteAsync(
            @"UPDATE fed_node SET node_name=@nodeName, node_level=@level, region_code=@regionCode,
                  base_url=@baseUrl, node_token=COALESCE(@nodeToken, node_token),
                  direction=@direction, status=@status WHERE id=@id",
            new { id, nodeName, level, regionCode, baseUrl, nodeToken, direction, status });
    }

    public async Task<int> SetNodeStatusAsync(long id, string status)
    {
        using var conn = _db.Create();
        return await conn.ExecuteAsync("UPDATE fed_node SET status=@status WHERE id=@id", new { id, status });
    }

    public async Task<int> DeleteNodeAsync(long id)
    {
        using var conn = _db.Create();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("DELETE FROM fed_sync_cursor WHERE peer_node_code=(SELECT node_code FROM fed_node WHERE id=@id)",
            new { id }, tx);
        var n = await conn.ExecuteAsync("DELETE FROM fed_node WHERE id=@id", new { id }, tx);
        tx.Commit();
        return n;
    }

    public async Task TouchNodeSyncAsync(long id, bool push, string? error)
    {
        using var conn = _db.Create();
        var col = push ? "last_push_at" : "last_pull_at";
        await conn.ExecuteAsync(
            $"UPDATE fed_node SET {col}=NOW(3), last_error=@error WHERE id=@id", new { id, error });
    }

    // ---------------- 上报批次 ----------------

    /// <summary>记录上报批次 (幂等, 重复 batch_no 覆盖计数与载荷)。</summary>
    public async Task<long> UpsertReportAsync(string sourceNodeCode, string batchNo, string periodDate,
        int metricCount, int recordCount, string? payloadJson)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO fed_report (source_node_code, batch_no, period_date, metric_count, record_count, payload)
              VALUES (@sourceNodeCode, @batchNo, @periodDate, @metricCount, @recordCount, @payloadJson)
              ON DUPLICATE KEY UPDATE period_date=VALUES(period_date), metric_count=VALUES(metric_count),
                  record_count=VALUES(record_count), payload=VALUES(payload), received_at=NOW(3), id=LAST_INSERT_ID(id);
              SELECT LAST_INSERT_ID();",
            new { sourceNodeCode, batchNo, periodDate, metricCount, recordCount, payloadJson });
    }

    public async Task<(List<FedReportRow> Items, long Total)> QueryReportsAsync(string? source, int page, int size)
    {
        using var conn = _db.Create();
        var where = string.IsNullOrWhiteSpace(source) ? "" : "WHERE source_node_code=@source";
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM fed_report {where}", new { source });
        var items = (await conn.QueryAsync<FedReportRow>(
            $@"SELECT id AS Id, source_node_code AS SourceNodeCode, batch_no AS BatchNo,
                      DATE_FORMAT(period_date,'%Y-%m-%d') AS PeriodDate,
                      metric_count AS MetricCount, record_count AS RecordCount, received_at AS ReceivedAt
               FROM fed_report {where} ORDER BY id DESC LIMIT @size OFFSET @offset",
            new { source, size, offset = (page - 1) * size })).ToList();
        return (items, total);
    }

    // ---------------- 汇总指标 ----------------

    public async Task UpsertMetricsAsync(string sourceNodeCode, IEnumerable<Dtos.FedMetricDto> metrics)
    {
        using var conn = _db.Create();
        var rows = metrics.Select(m => new
        {
            sourceNodeCode,
            statDate = m.StatDate,
            m.UserCount, m.ActiveUserCount, m.NewUserCount, m.SupplyCount, m.DemandCount,
            m.OrderCount, m.TradeAmount, m.MessageCount, m.ConversationCount,
        }).ToList();
        if (rows.Count == 0) return;
        await conn.ExecuteAsync(
            @"INSERT INTO fed_daily_metric
                (source_node_code, stat_date, user_count, active_user_count, new_user_count, supply_count,
                 demand_count, order_count, trade_amount, message_count, conversation_count)
              VALUES (@sourceNodeCode, @statDate, @UserCount, @ActiveUserCount, @NewUserCount, @SupplyCount,
                 @DemandCount, @OrderCount, @TradeAmount, @MessageCount, @ConversationCount)
              ON DUPLICATE KEY UPDATE user_count=VALUES(user_count), active_user_count=VALUES(active_user_count),
                 new_user_count=VALUES(new_user_count), supply_count=VALUES(supply_count),
                 demand_count=VALUES(demand_count), order_count=VALUES(order_count),
                 trade_amount=VALUES(trade_amount), message_count=VALUES(message_count),
                 conversation_count=VALUES(conversation_count)",
            rows);
    }

    public async Task<List<FedMetricRow>> QueryMetricsAsync(string? source, DateTime from, DateTime to)
    {
        using var conn = _db.Create();
        var where = string.IsNullOrWhiteSpace(source) ? "" : "AND source_node_code=@source";
        return (await conn.QueryAsync<FedMetricRow>(
            $@"SELECT source_node_code AS SourceNodeCode, DATE_FORMAT(stat_date,'%Y-%m-%d') AS StatDate,
                      user_count AS UserCount, active_user_count AS ActiveUserCount, new_user_count AS NewUserCount,
                      supply_count AS SupplyCount, demand_count AS DemandCount, order_count AS OrderCount,
                      trade_amount AS TradeAmount, message_count AS MessageCount, conversation_count AS ConversationCount
               FROM fed_daily_metric
               WHERE stat_date BETWEEN @from AND @to {where}
               ORDER BY stat_date",
            new { from, to, source })).ToList();
    }

    // ---------------- 业务明细 ----------------

    public async Task UpsertRecordsAsync(string sourceNodeCode, IEnumerable<Dtos.FedRecordDto> records)
    {
        using var conn = _db.Create();
        var rows = records.Select(r => new
        {
            sourceNodeCode,
            recordType = r.Type,
            externalId = r.ExternalId,
            regionCode = r.RegionCode,
            occurredAt = r.OccurredAt,
            data = r.Data.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "{}" : r.Data.GetRawText(),
        }).ToList();
        if (rows.Count == 0) return;
        await conn.ExecuteAsync(
            @"INSERT INTO fed_business_record
                (source_node_code, record_type, external_id, region_code, occurred_at, data)
              VALUES (@sourceNodeCode, @recordType, @externalId, @regionCode, @occurredAt, @data)
              ON DUPLICATE KEY UPDATE region_code=VALUES(region_code), occurred_at=VALUES(occurred_at), data=VALUES(data)",
            rows);
    }

    public async Task<List<FedRecordRow>> QueryRecordsAsync(string? source, string? type,
        DateTime? from, DateTime? to, int limit, int offset)
    {
        using var conn = _db.Create();
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(source)) clauses.Add("source_node_code=@source");
        if (!string.IsNullOrWhiteSpace(type)) clauses.Add("record_type=@type");
        if (from.HasValue) clauses.Add("occurred_at>=@from");
        if (to.HasValue) clauses.Add("occurred_at<=@to");
        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        return (await conn.QueryAsync<FedRecordRow>(
            $@"SELECT id AS Id, source_node_code AS SourceNodeCode, record_type AS Type, external_id AS ExternalId,
                      region_code AS RegionCode, occurred_at AS OccurredAt, data AS Data, received_at AS ReceivedAt
               FROM fed_business_record {where}
               ORDER BY occurred_at DESC LIMIT @limit OFFSET @offset",
            new { source, type, from, to, limit, offset })).ToList();
    }

    // ---------------- 多跳转发 (把已接收的下级数据再向上转发) ----------------

    /// <summary>导出 [from,to] 内"非本节点来源"的按天指标 (供中间节点向上层转发)。</summary>
    public async Task<List<FedMetricRow>> ExportForwardMetricsAsync(DateTime from, DateTime to, string selfCode)
    {
        using var conn = _db.Create();
        return (await conn.QueryAsync<FedMetricRow>(
            @"SELECT source_node_code AS SourceNodeCode, DATE_FORMAT(stat_date,'%Y-%m-%d') AS StatDate,
                     user_count AS UserCount, active_user_count AS ActiveUserCount, new_user_count AS NewUserCount,
                     supply_count AS SupplyCount, demand_count AS DemandCount, order_count AS OrderCount,
                     trade_amount AS TradeAmount, message_count AS MessageCount, conversation_count AS ConversationCount
              FROM fed_daily_metric
              WHERE stat_date BETWEEN @from AND @to AND source_node_code <> @selfCode
              ORDER BY stat_date",
            new { from, to, selfCode })).ToList();
    }

    /// <summary>导出 received_at > since 的"非本节点来源"明细 (按接收时间推进, 供中间节点向上层转发)。</summary>
    public async Task<List<FedRecordRow>> ExportForwardRecordsAsync(DateTime sinceReceived, int limit, string selfCode)
    {
        using var conn = _db.Create();
        return (await conn.QueryAsync<FedRecordRow>(
            @"SELECT id AS Id, source_node_code AS SourceNodeCode, record_type AS Type, external_id AS ExternalId,
                     region_code AS RegionCode, occurred_at AS OccurredAt, data AS Data, received_at AS ReceivedAt
              FROM fed_business_record WHERE received_at > @sinceReceived AND source_node_code <> @selfCode
              ORDER BY received_at ASC LIMIT @limit",
            new { sinceReceived, limit, selfCode })).ToList();
    }

    // ---------------- 汇聚 ----------------

    public async Task<List<FedNodeStatRow>> AggregateByNodeAsync(DateTime from, DateTime to,
        IReadOnlyCollection<string>? sources = null)
    {
        // sources=null 表示不过滤; sources 为空集合表示"限定为空"→ 无结果
        if (sources is { Count: 0 }) return new();
        var filter = sources is null ? "" : " AND source_node_code IN @sources";
        using var conn = _db.Create();
        return (await conn.QueryAsync<FedNodeStatRow>(
            $@"SELECT source_node_code AS SourceNodeCode, SUM(order_count) AS OrderCount, SUM(trade_amount) AS TradeAmount
               FROM fed_daily_metric WHERE stat_date BETWEEN @from AND @to{filter}
               GROUP BY source_node_code ORDER BY OrderCount DESC",
            new { from, to, sources })).ToList();
    }

    public async Task<List<FedAggPointRow>> AggregateByDayAsync(DateTime from, DateTime to,
        IReadOnlyCollection<string>? sources = null)
    {
        if (sources is { Count: 0 }) return new();
        var filter = sources is null ? "" : " AND source_node_code IN @sources";
        using var conn = _db.Create();
        return (await conn.QueryAsync<FedAggPointRow>(
            $@"SELECT DATE_FORMAT(stat_date,'%Y-%m-%d') AS StatDate, SUM(order_count) AS OrderCount,
                     SUM(trade_amount) AS TradeAmount, SUM(user_count) AS UserCount,
                     SUM(supply_count) AS SupplyCount, SUM(demand_count) AS DemandCount
               FROM fed_daily_metric WHERE stat_date BETWEEN @from AND @to{filter}
               GROUP BY stat_date ORDER BY stat_date",
            new { from, to, sources })).ToList();
    }

    // ---------------- 拉取游标 ----------------

    public async Task<DateTime?> GetCursorAsync(string peerNodeCode, string cursorType)
    {
        using var conn = _db.Create();
        var raw = await conn.ExecuteScalarAsync<string?>(
            "SELECT last_cursor FROM fed_sync_cursor WHERE peer_node_code=@peerNodeCode AND cursor_type=@cursorType",
            new { peerNodeCode, cursorType });
        return DateTime.TryParse(raw, out var dt) ? dt : null;
    }

    public async Task SetCursorAsync(string peerNodeCode, string cursorType, DateTime cursor)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            @"INSERT INTO fed_sync_cursor (peer_node_code, cursor_type, last_cursor)
              VALUES (@peerNodeCode, @cursorType, @cursor)
              ON DUPLICATE KEY UPDATE last_cursor=VALUES(last_cursor)",
            new { peerNodeCode, cursorType, cursor = cursor.ToString("yyyy-MM-dd HH:mm:ss.fff") });
    }

    // ---------------- 跨实例互斥 (后台定时同步只允许一个副本执行) ----------------

    /// <summary>
    /// 非阻塞获取 MySQL 咨询锁; 获取不到返回 null (说明已有另一副本在执行同步)。
    /// 返回的句柄持有同一连接, Dispose 时释放锁并归还连接。
    /// </summary>
    public async Task<FedSyncLock?> TryAcquireSyncLockAsync(string name, CancellationToken ct = default)
    {
        name = name.Length <= 64 ? name : name[..64];
        var conn = _db.Create();
        try
        {
            conn.Open();
            var acquired = await conn.ExecuteScalarAsync<int?>("SELECT GET_LOCK(@name, 0)", new { name });
            if (acquired == 1) return new FedSyncLock(conn, name);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
        conn.Dispose();
        return null;
    }
}

/// <summary>持有咨询锁的连接句柄; Dispose 时在<b>同一连接</b>上释放锁再关闭连接。</summary>
public sealed class FedSyncLock : IDisposable
{
    private readonly IDbConnection _conn;
    private readonly string _name;
    public FedSyncLock(IDbConnection conn, string name)
    {
        _conn = conn;
        _name = name;
    }

    public void Dispose()
    {
        try { _conn.Execute("SELECT RELEASE_LOCK(@name)", new { name = _name }); }
        catch { /* 释放失败交由连接关闭兜底 */ }
        _conn.Dispose();
    }
}
