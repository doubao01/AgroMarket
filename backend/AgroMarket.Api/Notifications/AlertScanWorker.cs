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
        // 互斥统一由 AlertService.ScanAsync 内部的命名锁负责(手动触发也走同一条路径);
        // 这里曾再取一把名字不同的锁, 既不起作用又让人误以为有两层保护
        var sw = Stopwatch.StartNew();
        // 日级规则(成交额波动/联动滞后)每 24h 执行一次
        var dayRules = DateTime.Now - _lastDailyRun >= TimeSpan.FromHours(24);
        var summary = await _alerts.ScanAsync(dayRules);
        if (summary.Busy)
        {
            _logger.LogDebug("alert scan skipped: another instance is scanning");
            return;
        }
        if (dayRules) _lastDailyRun = DateTime.Now;
        if (summary.Executed > 0)
            _logger.LogInformation(
                "alert scan done: executed={Executed} hit={Hit} suppressed={Suppressed} errors={Errors} elapsed={Elapsed}ms",
                summary.Executed, summary.Hit, summary.Suppressed, summary.Errors.Count, sw.ElapsedMilliseconds);
    }
}
