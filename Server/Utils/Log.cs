using System.Diagnostics;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;

namespace DiscordRaidFeed.Server.Utils;

[Injectable(InjectionType.Singleton)]
public sealed class Log(ISptLogger<Log> logger)
{
    public void Info(string message) => logger.Info($"[DiscordRaidFeed] {message}");
    public void Warning(string message) => logger.Warning($"[DiscordRaidFeed] {message}");
    public void Error(string message, Exception? exception = null) => logger.Error($"[DiscordRaidFeed] {message}{(exception is null ? string.Empty : $" {exception.Message}")}");

    [Conditional("DEBUG")]
    public void Debug(string message) => logger.Debug($"[DiscordRaidFeed] {message}");
}
