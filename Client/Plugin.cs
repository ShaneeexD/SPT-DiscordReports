using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DiscordRaidFeed.Client.Patches;
using DiscordRaidFeed.Client.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DiscordRaidFeed.Client;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "com.shaneeexd.discord-raid-feed";
    public const string Name = "Discord Raid Feed Client";
    public const string Version = "1.0.0";
    internal static ManualLogSource Log = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<string> ServerUrl = null!;
    internal static ConfigEntry<bool> Screenshots = null!;
    internal static ConfigEntry<bool> LootEvents = null!;
    internal static ConfigEntry<bool> BossKillEvents = null!;
    internal static ConfigEntry<bool> QuestEvents = null!;
    internal static ConfigEntry<bool> LevelUpEvents = null!;

    // Profile IDs with edition "SPT Developer" (and username != "Dev2") — events from these are skipped.
    internal static readonly System.Collections.Generic.HashSet<string> DevProfileIds = new();

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Enable client-side Discord raid event reporting.");
        ServerUrl = Config.Bind("General", "ServerUrl", "https://127.0.0.1:6969", "Local SPT server URL.");
        Screenshots = Config.Bind("Screenshots", "Enabled", true, "Capture screenshots for supported events.");
        LootEvents = Config.Bind("Events", "Loot", true, "Report picked-up loot. Remote configuration still controls thresholds.");
        BossKillEvents = Config.Bind("Events", "BossKills", true, "Report boss kills.");
        QuestEvents = Config.Bind("Events", "Quests", true, "Report completed quests.");
        LevelUpEvents = Config.Bind("Events", "LevelUps", true, "Report level-ups.");

        ScanDevProfiles();

        try
        {
            Log.LogInfo("Enabling patches...");
            new RaidStartPatch().Enable();
            new RaidEndPatch().Enable();
            new RaidStopPatch().Enable();
            new PlayerDeathPatch().Enable();
            new BossKillPatch().Enable();
            new LootPickupPatch().Enable();
            new QuestCompletionPatch().Enable();
            Log.LogInfo("All patches enabled. Creating reporter...");
            var host = new GameObject("DiscordRaidFeedClient");
            DontDestroyOnLoad(host);
            host.AddComponent<ClientEventReporter>();
            Log.LogInfo($"{Name} {Version} loaded.");
        }
        catch (Exception ex) { Log.LogError($"Failed to load client: {ex}"); }
    }

    private static void ScanDevProfiles()
    {
        try
        {
            var sptRoot = Path.GetDirectoryName(Application.dataPath);
            if (sptRoot == null) return;
            var profilesDir = Path.Combine(sptRoot, "user", "profiles");
            if (!Directory.Exists(profilesDir))
            {
                Log.LogInfo("[DiscordRaidFeed] Profiles directory not found, skipping dev profile check.");
                return;
            }

            foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
            {
                try
                {
                    var root = JObject.Parse(File.ReadAllText(file));
                    var info = root["info"] as JObject;
                    if (info == null) continue;

                    var edition = info["edition"]?.ToString();
                    var username = info["username"]?.ToString();
                    var id = info["id"]?.ToString() ?? Path.GetFileNameWithoutExtension(file);

                    if (string.IsNullOrEmpty(edition)) continue;

                    if (edition == "SPT Developer" && username != "Dev2")
                    {
                        DevProfileIds.Add(id);
                        Log.LogInfo($"[DiscordRaidFeed] Dev profile detected: id={id}, username={username} — events will be skipped.");
                    }
                }
                catch { }
            }

            Log.LogInfo($"[DiscordRaidFeed] Profile scan complete. {DevProfileIds.Count} dev profile(s) found.");
        }
        catch (Exception ex) { Log.LogError($"[DiscordRaidFeed] Failed to scan profiles: {ex}"); }
    }
}
