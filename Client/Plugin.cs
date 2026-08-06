using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SPTDiscordReports.Client.Services;
using UnityEngine;

namespace SPTDiscordReports.Client;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "com.shaneeexd.spt-discord-raid-feed";
    public const string Name = "SPT Discord Raid Feed Client";
    public const string Version = "1.0.0";
    internal static ManualLogSource Log = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<string> ServerUrl = null!;
    internal static ConfigEntry<bool> Screenshots = null!;
    internal static ConfigEntry<bool> LootEvents = null!;
    internal static ConfigEntry<bool> BossKillEvents = null!;
    internal static ConfigEntry<bool> QuestEvents = null!;
    internal static ConfigEntry<bool> LevelUpEvents = null!;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Enable client-side Discord raid event reporting.");
        ServerUrl = Config.Bind("General", "ServerUrl", "http://127.0.0.1:6969", "Local SPT server URL.");
        Screenshots = Config.Bind("Screenshots", "Enabled", true, "Capture screenshots for supported events.");
        LootEvents = Config.Bind("Events", "Loot", true, "Report picked-up loot. Remote configuration still controls thresholds.");
        BossKillEvents = Config.Bind("Events", "BossKills", true, "Report boss kills.");
        QuestEvents = Config.Bind("Events", "Quests", true, "Report completed quests.");
        LevelUpEvents = Config.Bind("Events", "LevelUps", true, "Report level-ups.");

        try
        {
            new Harmony(Guid).PatchAll();
            var host = new GameObject("SPTDiscordReportsClient");
            DontDestroyOnLoad(host);
            host.AddComponent<ClientEventReporter>();
            Log.LogInfo($"{Name} {Version} loaded.");
        }
        catch (Exception ex) { Log.LogError($"Failed to load client: {ex}"); }
    }
}
