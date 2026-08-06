using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
using EFT.Quests;
using HarmonyLib;
using SPTDiscordReports.Client.Services;

namespace SPTDiscordReports.Client.Patches;

[HarmonyPatch(typeof(BaseLocalGame<EftGamePlayerOwner>), "Stop", new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) })]
internal static class RaidStopPatch
{
    [HarmonyPrefix]
    private static void Prefix(ExitStatus exitStatus)
    {
        var reporter = UnityEngine.Object.FindObjectOfType<ClientEventReporter>();
        if (reporter == null) return;
        if (exitStatus == ExitStatus.Survived || exitStatus == ExitStatus.Runner) reporter.ReportExtract(exitStatus);
        else reporter.ReportDeath(exitStatus);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnBeenKilledByAggressor), new[] { typeof(IPlayer), typeof(DamageInfo), typeof(EBodyPart), typeof(EDamageType) })]
internal static class BossKillPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance, IPlayer aggressor, DamageInfo damageInfo, EBodyPart bodyPart)
    {
        var reporter = UnityEngine.Object.FindObjectOfType<ClientEventReporter>();
        if (reporter == null || __instance?.Profile?.Info?.Settings == null) return;
        var role = __instance.Profile.Info.Settings.Role.ToString();
        if (role.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 || role.IndexOf("follower", StringComparison.OrdinalIgnoreCase) >= 0)
            reporter.ReportBossKill(__instance, aggressor, damageInfo, bodyPart);
    }
}

[HarmonyPatch(typeof(QuestControllerClientBackend), nameof(QuestControllerClientBackend.FinishQuest))]
internal static class QuestCompletionPatch
{
    [HarmonyPostfix]
    private static void Postfix(Quest quest)
    {
        if (quest == null || quest.QuestStatus != EQuestStatus.Success) return;
        UnityEngine.Object.FindObjectOfType<ClientEventReporter>()?.ReportQuest(quest.Template?.Name ?? quest.Id, "Unknown");
    }
}
