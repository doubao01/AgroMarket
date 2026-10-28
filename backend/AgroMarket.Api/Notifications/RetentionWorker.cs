using Dapper;

namespace AgroMarket.Api.Notifications;

/// <summary>
/// 数据留存清理。按配置的期限物理删除过期数据。
///
/// 为什么必须有: 聊天正文、站内通知、审计日志、联邦明细此前只增不减 —— 用户的
/// 聊天记录会永久留在库里, "存储期限最小化"这条合规要求一条都满足不了。
///
/// 默认 <b>关闭</b> (Retention:Enabled=false)。这是删除数据的后台任务, 期限取值
/// 属于业务/合规决定, 不该由代码替业务方拍板: 打开开关并给出期限后才真正开始清理。
///
/// 每张表单独配置天数, 0 或负数表示该类数据不清理(便于"只清通知不动聊天")。
/// </summary>
public class RetentionWorker : BackgroundService
{
    private readonly IDbConnectionFactory _db;
    private readonly RetentionSettings _settings;
    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(IDbConnectionFactory db, RetentionSettings settings, ILogger<RetentionWorker> logger)
    {
        _db = db; _settings = settings; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("[留存] 未启用 (后台设置 Enabled=0), 不做任何清理");
            return;
        }

        // 启动时先跑一次, 之后每小时一次: 这类清理对时效不敏感, 频率过高只会白占连接
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[留存] 清理失败, 下一轮重试");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var conn = _db.Create();
        var total = 0;
        // 分批删: 一次 DELETE 几十万行会长时间持锁, 影响线上读写
        total += await DeleteOlderThanAsync(conn, "chat_message", "created_at", _settings.ChatMessageDays, ct);
        total += await DeleteOlderThanAsync(conn, "user_message", "created_at", _settings.NotificationDays, ct);
        total += await DeleteOlderThanAsync(conn, "sys_operation_log", "created_at", _settings.OperationLogDays, ct);
        total += await DeleteOlderThanAsync(conn, "fed_report", "received_at", _settings.FederationDays, ct);
        total += await DeleteOlderThanAsync(conn, "sys_sms_code", "created_at", _settings.SmsCodeDays, ct);
        if (total > 0) _logger.LogInformation("[留存] 本轮清理 {Count} 行", total);
    }


    private async Task<int> DeleteOlderThanAsync(
        System.Data.IDbConnection conn, string table, string timeColumn, int days, CancellationToken ct)
    {
        if (days <= 0) return 0;
        var removed = 0;
        while (!ct.IsCancellationRequested)
        {
            var n = await conn.ExecuteAsync(
                $"DELETE FROM {table} WHERE {timeColumn} < DATE_SUB(NOW(3), INTERVAL @days DAY) LIMIT 2000",
                new { days });
            removed += n;
            if (n < 2000) break;
        }
        if (removed > 0) _logger.LogInformation("[留存] {Table} 清理 {Count} 行 (超过 {Days} 天)", table, removed, days);
        return removed;
    }
}
