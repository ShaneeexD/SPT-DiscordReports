using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTDiscordReports.Server.Services;

namespace SPTDiscordReports.Server.Patches;

public sealed class RaidStartPatch : AbstractPatch
{
    protected override MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.StartLocalRaidAsync));
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, StartLocalRaidRequestData request)
    {
        Dependencies.Tracker?.OnRaidStart(sessionId.ToString(), request.Location ?? "Unknown");
        Dependencies.Logger?.Info($"[DiscordRaidFeed] Raid start tracked: session={sessionId}, location={request.Location}");
    }

    internal static DependenciesHolder? Dependencies;
}

public sealed class RaidEndPatch : AbstractPatch
{
    protected override MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.EndLocalRaidAsync));
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, EndLocalRaidRequestData request)
    {
        if (Dependencies == null) return;
        var result = request.Results;
        if (result == null)
        {
            Dependencies.Logger?.Warning($"[DiscordRaidFeed] Raid end: results is null for session={sessionId}");
            return;
        }

        var session = sessionId.ToString();
        var (map, startedAt) = Dependencies.Tracker?.OnRaidEnd(session) ?? ("Unknown", default);
        var playTime = result.PlayTime ?? 0;
        if (playTime <= 0 && startedAt != default)
            playTime = (DateTime.UtcNow - startedAt).TotalSeconds;

        // Server-side raid end is tracked for timing only.
        // Death/extract events are sent by the client mod with richer data (killer name, gear value, loot value, screenshots).
        // If the client mod is not installed, no death/extract event will be sent.
        Dependencies.Logger?.Info($"[DiscordRaidFeed] Raid ended (server-side): player={result.Profile?.Info?.Nickname ?? "Unknown"}, map={map}, playTime={playTime}s, result={result.Result}. Client mod handles Discord notification.");
    }

    internal static DependenciesHolder? Dependencies;
}

internal sealed class DependenciesHolder(RaidTracker tracker, EventManager events, SPTDiscordReports.Server.Utils.Log log)
{
    public RaidTracker Tracker { get; } = tracker;
    public EventManager Events { get; } = events;
    public SPTDiscordReports.Server.Utils.Log Logger { get; } = log;
}
