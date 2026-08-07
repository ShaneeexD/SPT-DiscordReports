using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI.SessionEnd;
using HarmonyLib;
using SPT.Reflection.Patching;
using DiscordRaidFeed.Client.Services;

namespace DiscordRaidFeed.Client.Patches;

internal class RaidStartPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod("OnGameStarted", BindingFlags.Public | BindingFlags.Instance);
    }

    [PatchPostfix]
    private static void PatchPostfix(GameWorld __instance)
    {
        try
        {
            if (__instance.LocationId == "hideout") return;
            Plugin.Log.LogInfo($"[DiscordRaidFeed] RaidStartPatch fired: map={__instance.LocationId}, Instance={ClientEventReporter.Instance != null}");
            var reporter = ClientEventReporter.EnsureInstance();
            reporter?.OnRaidStart(__instance);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] RaidStartPatch error: {ex}"); }
    }
}

internal class RaidEndPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod("OnDestroy", BindingFlags.Public | BindingFlags.Instance);
    }

    [PatchPostfix]
    private static void PatchPostfix()
    {
        try
        {
            Plugin.Log.LogInfo($"[DiscordRaidFeed] RaidEndPatch fired (GameWorld.OnDestroy), Instance={ClientEventReporter.Instance != null}");
            ClientEventReporter.Instance?.OnRaidEnd();
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] RaidEndPatch error: {ex}"); }
    }
}

internal class RaidStopPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        // Patch SessionResultExitStatus.Show which receives ExitStatus as a parameter.
        // This is the post-raid screen that shows "Survived"/"Killed"/"Run Through".
        var showMethod = AccessTools.Method(typeof(SessionResultExitStatus), "Show", new[] {
            typeof(Profile), typeof(PlayerVisualRepresentation), typeof(ESideType),
            typeof(ExitStatus), typeof(TimeSpan), typeof(IEftSession), typeof(bool)
        });
        if (showMethod == null)
            Plugin.Log.LogWarning("[DiscordRaidFeed] Could not find SessionResultExitStatus.Show method");
        return showMethod!;
    }

    [PatchPrefix]
    private static void PatchPrefix(ExitStatus exitStatus)
    {
        try
        {
            Plugin.Log.LogInfo($"[DiscordRaidFeed] RaidStopPatch fired (SessionResultExitStatus.Show): exitStatus={exitStatus}");
            ClientEventReporter.Instance?.OnRaidStop(exitStatus);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] RaidStopPatch error: {ex}"); }
    }
}

internal class PlayerDeathPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Player), nameof(Player.OnDead));
    }

    [PatchPostfix]
    private static void PatchPostfix(Player __instance, EDamageType damageType)
    {
        try
        {
            if (__instance.Location == "hideout") return;
            if (!ReferenceEquals(__instance, Singleton<GameWorld>.Instance?.MainPlayer)) return;
            // Local player died — cache killer name and mark as not alive for raid end event
            Plugin.Log.LogInfo($"[DiscordRaidFeed] PlayerDeathPatch fired: damageType={damageType}");
            ClientEventReporter.Instance?.OnPlayerDeath(__instance);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] PlayerDeathPatch error: {ex}"); }
    }
}

internal class BossKillPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Player), nameof(Player.OnBeenKilledByAggressor));
    }

    [PatchPostfix]
    private static void PatchPostfix(Player __instance, IPlayer aggressor, DamageInfo damageInfo, EBodyPart bodyPart, EDamageType lethalDamageType)
    {
        try
        {
            if (__instance.Location == "hideout") return;
            var role = __instance.Profile?.Info?.Settings.Role.ToString() ?? "";
            if (role.IndexOf("boss", StringComparison.OrdinalIgnoreCase) < 0 && role.IndexOf("follower", StringComparison.OrdinalIgnoreCase) < 0) return;
            Plugin.Log.LogInfo($"[DiscordRaidFeed] BossKillPatch fired: boss={role}");
            ClientEventReporter.Instance?.ReportBossKill(__instance, aggressor, damageInfo, bodyPart);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] BossKillPatch error: {ex}"); }
    }
}

internal class LootPickupPatch : ModulePatch
{
    // Captured in prefix (before item moves to inventory), used in postfix
    private static string? _pendingScreenshotPath;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Player), nameof(Player.OnItemAddedOrRemoved));
    }

    [PatchPrefix]
    private static void PatchPrefix(Player __instance, Item item, bool added)
    {
        try
        {
            _pendingScreenshotPath = null;
            if (__instance.Location == "hideout") return;
            if (!added || item == null) return;
            if (!ReferenceEquals(__instance, Singleton<GameWorld>.Instance?.MainPlayer)) return;
            if (!item.SpawnedInSession) return;
            if (!Plugin.Screenshots.Value) return;

            // Capture screenshot BEFORE the item is added to inventory (while it's still visible in the world)
            _pendingScreenshotPath = ClientEventReporter.Instance?.CaptureLootScreenshot();
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] LootPickupPatch prefix error: {ex}"); }
    }

    [PatchPostfix]
    private static void PatchPostfix(Player __instance, Item item, ItemAddress location, bool added)
    {
        try
        {
            if (__instance.Location == "hideout") return;
            if (!added || item == null) return;
            if (!ReferenceEquals(__instance, Singleton<GameWorld>.Instance?.MainPlayer)) return;
            if (!item.SpawnedInSession) return;

            Plugin.Log.LogInfo($"[DiscordRaidFeed] LootPickupPatch: item={item.LocalizedName()}, tpl={item.TemplateId}, fir=True, location={location?.Container?.ID}");

            ClientEventReporter.Instance?.ReportLoot(item, _pendingScreenshotPath);
            _pendingScreenshotPath = null;
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] LootPickupPatch error: {ex}"); }
    }
}

internal class QuestCompletionPatch : ModulePatch
{
    private static readonly Dictionary<string, string> TraderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["54cb50c76803fa8b248b4571"] = "Prapor",
        ["54cb57776803fa99248b456e"] = "Therapist",
        ["579dc571d53a0658a154fbec"] = "Fence",
        ["58330581ace78e27b8b10cee"] = "Skier",
        ["5935c25fb3acc3127c3d8cd9"] = "Peacekeeper",
        ["5a7c2eca46aef81a7ca2145d"] = "Mechanic",
        ["5ac3b934156ae10c4430e83c"] = "Ragman",
        ["5c0647fdd443bc2504c2d371"] = "Jaeger",
        ["638f541a29ffd1183d187f57"] = "Lightkeeper",
        ["656f0f98d80a697f855d34b1"] = "BTR Driver",
        ["6617beeaa9cfa777ca915b7c"] = "Ref",
    };

    private static string TraderName(string? traderId) =>
        !string.IsNullOrWhiteSpace(traderId) && TraderNames.TryGetValue(traderId, out var name) ? name : "Unknown";

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(QuestControllerClientBackend), "FinishQuest");
    }

    [PatchPostfix]
    private static void PatchPostfix(Quest quest, bool runNetworkTransaction)
    {
        try
        {
            if (quest == null) return;
            var questName = quest.Template?.Name ?? quest.Id;
            var trader = TraderName(quest.Template?.TraderId);
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Quest completed: {questName} ({trader})");
            ClientEventReporter.Instance?.ReportQuest(questName, trader);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] QuestCompletionPatch error: {ex}"); }
    }
}
