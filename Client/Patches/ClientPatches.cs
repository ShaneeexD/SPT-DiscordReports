using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPTDiscordReports.Client.Services;

namespace SPTDiscordReports.Client.Patches;

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
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Player), nameof(Player.OnItemAddedOrRemoved));
    }

    [PatchPostfix]
    private static void PatchPostfix(Player __instance, Item item, ItemAddress location, bool added)
    {
        try
        {
            if (__instance.Location == "hideout") return;
            if (!added || item == null) return;
            if (!ReferenceEquals(__instance, Singleton<GameWorld>.Instance?.MainPlayer)) return;
            // Only report items found in raid (FIR) — skip items the player brought in
            if (!item.SpawnedInSession) return;

            Plugin.Log.LogInfo($"[DiscordRaidFeed] LootPickupPatch: item={item.LocalizedName()}, tpl={item.TemplateId}, fir=True, location={location?.Container?.ID}");

            ClientEventReporter.Instance?.ReportLoot(item);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] LootPickupPatch error: {ex}"); }
    }
}

internal class QuestCompletionPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(QuestControllerClientBackend), "FinishQuest");
    }

    [PatchPostfix]
    private static void PatchPostfix(Quest quest, bool runNetworkTransaction)
    {
        try
        {
            if (quest == null || quest.QuestStatus != EQuestStatus.Success) return;
            Plugin.Log.LogInfo($"[DiscordRaidFeed] QuestCompletionPatch fired: {quest.Template?.Name ?? quest.Id}");
            ClientEventReporter.Instance?.ReportQuest(quest.Template?.Name ?? quest.Id, "Unknown");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] QuestCompletionPatch error: {ex}"); }
    }
}
