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
    private readonly IOptionsMonitor<FederationOptions> _opt;
    private readonly ILogger<FederationSyncWorker> _logger;

    public FederationSyncWorker(IServiceScopeFactory scopes, IOptionsMonitor<FederationOptions> opt,
        ILogger<FederationSyncWorker> logger)
    {
        _scopes = scopes;
        _opt = opt;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动初期先等一会, 避免与冷启动 / 数据库初始化抢资源
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var opt = _opt.CurrentValue;
            var interval = TimeSpan.FromMinutes(Math.Max(1, opt.SyncIntervalMinutes));
            if (opt.IsSyncActive)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var fed = scope.ServiceProvider.GetRequiredService<FederationService>();
                    var push = await fed.PushAsync(null, stoppingToken);
                    var pull = await fed.PullAsync(null, stoppingToken);
                    _logger.LogInformation("联动定时同步完成: 推送 {Ps}/{Pt} 成功, 拉取 {Qs}/{Qt} 成功",
                        push.Succeeded, push.Targets, pull.Succeeded, pull.Targets);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "联动定时同步异常");
                }
            }
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
