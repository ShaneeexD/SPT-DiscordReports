using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils;
using SPTDiscordReports.Server.Config;
using SPTDiscordReports.Server.Discord;
using SPTDiscordReports.Server.Patches;
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
    RaidTracker raidTracker, EventManager events, Log log) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var path = modHelper.GetAbsolutePathToModFolder(typeof(Plugin).Assembly);
        config.Initialise(path);
        if (!config.Local.Enabled) { logger.Warning("[DiscordRaidFeed] Disabled in config.json."); return; }

        var deps = new DependenciesHolder(raidTracker, events, log);
        RaidStartPatch.Dependencies = deps;
        RaidEndPatch.Dependencies = deps;

        new RaidStartPatch().Enable();
        new RaidEndPatch().Enable();
        logger.Info("[DiscordRaidFeed] Harmony patches enabled on MatchController.StartLocalRaidAsync and EndLocalRaidAsync.");

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
        logger.Info("[DiscordRaidFeed] Loaded. Raid completion events are queued asynchronously; client integrations may post richer event payloads to /client/discordraidfeed/event.");
    }
}
