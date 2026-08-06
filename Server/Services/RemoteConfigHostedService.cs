using Microsoft.Extensions.Hosting;
using SPTDiscordReports.Server.Utils;

namespace SPTDiscordReports.Server.Services;

public sealed class RemoteConfigHostedService(ConfigService config, Log log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await config.RefreshAsync(stoppingToken).ConfigureAwait(false); }
            catch (Exception ex) { log.Error("Remote configuration refresh failed.", ex); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, config.Local.RefreshIntervalMinutes)), stoppingToken).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }
}
