using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers.Static;
using SPTarkov.Server.Core.Utils;
using SPTDiscordReports.Server.Config;
using SPTDiscordReports.Server.Discord;
using SPTDiscordReports.Server.Services;
using SPTDiscordReports.Server.Utils;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace SPTDiscordReports.Server;

public sealed record DiscordRaidFeedMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.shaneeexd.spt-discord-raid-feed";
    public string Name { get; init; } = "SPT Discord Raid Feed";
    public string Author { get; init; } = "ShaneeexD";
    public List<string> Contributors { get; init; } = [];
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string> Incompatibilities { get; init; } = [];
    public Dictionary<string, Range> ModDependencies { get; init; } = [];
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class Plugin(
    ISptLogger<Plugin> logger, ModHelper modHelper, ConfigService config, DiscordWebhookService discord,
    MatchStaticRouter matchRouter, EventManager events) : IOnLoad
{
    private readonly Dictionary<string, string> _raidMaps = new(StringComparer.OrdinalIgnoreCase);

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var path = modHelper.GetAbsolutePathToModFolder(typeof(Plugin).Assembly);
        config.Initialise(path);
        if (!config.Local.Enabled) { logger.Warning("[DiscordRaidFeed] Disabled in config.json."); return; }
        matchRouter.OnBeforeAction += (_, data) =>
        {
            if (data is StaticDynamicOnBeforeEventRequestData before && before.RequestData is StartLocalRaidRequestData start && !string.IsNullOrWhiteSpace(start.Location))
                _raidMaps[before.SessionId.ToString()] = start.Location;
        };
        matchRouter.OnAfterAction += (_, data) =>
        {
            if (data is not StaticDynamicOnAfterEventRequestData after || after.RequestData is not EndLocalRaidRequestData request || request.Results is null) return;
            var result = request.Results;
            var session = after.SessionId.ToString();
            _raidMaps.TryGetValue(session, out var map);
            _raidMaps.Remove(session);
            var type = result.Result == SPTarkov.Server.Core.Models.Enums.ExitStatus.SURVIVED ? Events.RaidEventType.Extract : Events.RaidEventType.Death;
            events.Publish(new Events.RaidEvent { Type = type, Player = result.Profile?.Info?.Nickname ?? "Unknown", Level = result.Profile?.Info?.Level ?? 0, Map = map ?? request.LocationTransit?.SptLastVisitedLocation ?? "Unknown", RaidTimeSeconds = result.PlayTime ?? 0, Fields = new() { ["Extract"] = result.ExitName ?? "Unknown", ["Killer"] = result.KillerId?.ToString() ?? "Unknown" } });
        };
        discord.Start();
        await config.RefreshAsync(cancellationToken);
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, config.Local.RefreshIntervalMinutes)), cancellationToken); await config.RefreshAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.Warning($"[DiscordRaidFeed] Remote refresh failed: {ex.Message}"); }
            }
        }, cancellationToken);
        logger.Info("Loaded. Raid completion events are queued asynchronously; client integrations may post richer event payloads to /client/discordraidfeed/event.");
    }
}
