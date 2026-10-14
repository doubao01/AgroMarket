using Dapper;
using System.Diagnostics;

namespace AgroMarket.Api.Notifications;

/// <summary>
/// 报警扫描后台任务: 按 alert_scan_interval_minutes(默认30,热生效) 执行规则扫描;
/// 通知频次由 alert_record 抑制窗口控制; GET_LOCK 保证多实例下仅一个实例扫描; 启动错峰 30~60s。
/// </summary>
public class AlertScanWorker : BackgroundService
{
    private readonly IDbConnectionFactory _db;
    private readonly AlertService _alerts;
    private readonly ILogger<AlertScanWorker> _logger;
    private readonly IConfiguration _cfg;

    public AlertScanWorker(IDbConnectionFactory db, AlertService alerts, ILogger<AlertScanWorker> logger, IConfiguration cfg)
    { _db = db; _alerts = alerts; _logger = logger; _cfg = cfg; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 错峰启动, 避免与其它 worker/多节点同时批量执行
        await Task.Delay(Random.Shared.Next(30, 61) * 1000, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "alert scan round failed");
            }
            await Task.Delay(IntervalMinutes() * 60 * 1000, stoppingToken);
        }
    }

    private int IntervalMinutes()
    {
        var v = _cfg.GetValue<int?>("Alert:ScanIntervalMinutes") ?? 30;
        return Math.Max(5, v);
    }

    private DateTime _lastDailyRun = DateTime.MinValue;

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        using var conn = _db.Create();
        // 多实例互斥: 非阻塞咨询锁, 取不到说明本轮已有实例在扫
        var locked = await conn.ExecuteScalarAsync<bool>("SELECT GET_LOCK('agro_market_alert_scan', 0)");
        if (!locked)
        {
            _logger.LogDebug("alert scan lock held by another instance, skip");
            return;
        }
        try
        {
            var sw = Stopwatch.StartNew();
            // 日级规则(成交额波动/联动滞后)每 24h 执行一次
            var dayRules = DateTime.Now - _lastDailyRun >= TimeSpan.FromHours(24);
            var summary = await _alerts.ScanAsync(dayRules);
            if (dayRules) _lastDailyRun = DateTime.Now;
            if (summary.Executed > 0)
                _logger.LogInformation(
                    "alert scan done: executed={Executed} hit={Hit} suppressed={Suppressed} errors={Errors} elapsed={Elapsed}ms",
                    summary.Executed, summary.Hit, summary.Suppressed, summary.Errors.Count, sw.ElapsedMilliseconds);
        }
        finally
        {
            await conn.ExecuteAsync("SELECT RELEASE_LOCK('agro_market_alert_scan')");
        }
    }
}
