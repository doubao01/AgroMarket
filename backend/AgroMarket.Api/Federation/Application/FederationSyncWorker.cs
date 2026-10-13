using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Application;

/// <summary>
/// 联动后台定时同步: 按 Federation:SyncIntervalMinutes 周期执行一次推送 + 拉取。
/// 仅当 Federation 配置完整 (Enabled + AutoSync + 身份齐备) 时才工作; 失败只记日志不影响主流程。
/// </summary>
public class FederationSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly FederationOptions _opt;
    private readonly FederationSettings _settings;
    private readonly ILogger<FederationSyncWorker> _logger;

    public FederationSyncWorker(IServiceScopeFactory scopes, IOptions<FederationOptions> opt,
        FederationSettings settings, ILogger<FederationSyncWorker> logger)
    {
        _scopes = scopes;
        _opt = opt.Value;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动初期错峰等待 (10~40s), 避免多节点同时冷启动 / 抢数据库资源
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(10, 41)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.SyncIntervalMinutes));
            if (_settings.IsSyncActive)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var fed = scope.ServiceProvider.GetRequiredService<FederationService>();
                    var repo = scope.ServiceProvider.GetRequiredService<FederationRepository>();
                    // 跨副本互斥: 仅一个实例执行本轮同步, 避免多副本重复推拉与游标竞争
                    using var gate = await repo.TryAcquireSyncLockAsync($"agro_fed_sync:{_opt.NodeCode}", stoppingToken);
                    if (gate is null)
                    {
                        _logger.LogDebug("联动定时同步: 未获取到分布式锁 (其它副本执行中), 本周期跳过");
                    }
                    else
                    {
                        var push = await fed.PushAsync(null, stoppingToken);
                        var pull = await fed.PullAsync(null, stoppingToken);
                        _logger.LogInformation("联动定时同步完成: 推送 {Ps}/{Pt} 成功, 拉取 {Qs}/{Qt} 成功",
                            push.Succeeded, push.Targets, pull.Succeeded, pull.Targets);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "联动定时同步异常");
                }
            }
            // 周期抖动 (0~20%): 多节点避免同一时刻批量推拉造成瞬时压力
            var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, Math.Max(30, (int)(interval.TotalSeconds * 0.2)) + 1));
            try { await Task.Delay(interval + jitter, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
