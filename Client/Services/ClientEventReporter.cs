using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HandBook;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace SPTDiscordReports.Client.Services;

public sealed class ClientEventReporter : MonoBehaviour
{
    private readonly Queue<RaidEventPayload> _pending = new Queue<RaidEventPayload>();
    private readonly HashSet<string> _reportedLoot = new HashSet<string>();
    private float _raidStartedAt;
    private int _lastLevel;
    private bool _wasInRaid;
    private bool _sending;
    private GameWorld _subscribedWorld;
    private bool _raidEnded;

    private void Update()
    {
        if (!Plugin.Enabled.Value) return;
        var world = Singleton<GameWorld>.Instance;
        var player = world?.MainPlayer;
        if (world == null || player == null) return;
        if (!ReferenceEquals(_subscribedWorld, world))
        {
            if (_subscribedWorld != null) _subscribedWorld.OnTakeItem -= ReportLoot;
            _subscribedWorld = world;
            _subscribedWorld.OnTakeItem += ReportLoot;
        }
        if (!_wasInRaid) { _wasInRaid = true; _raidEnded = false; _raidStartedAt = Time.time; _lastLevel = player.Profile?.Info?.Level ?? 0; }
        if (Plugin.LevelUpEvents.Value && player.Profile?.Info?.Level > _lastLevel)
        {
            _lastLevel = player.Profile.Info.Level;
            Enqueue(new RaidEventPayload { Type = RaidEventType.LevelUp, Player = NameOf(player), Level = _lastLevel, Map = world.LocationId, Fields = new Dictionary<string, string> { ["New Level"] = _lastLevel.ToString() } });
        }
        if (_pending.Count > 0 && !_sending) { _sending = true; StartCoroutine(SendNext()); }
    }

    public void ReportDeath(ExitStatus status)
    {
        if (_raidEnded) return;
        _raidEnded = true;
        var world = Singleton<GameWorld>.Instance;
        var player = world?.MainPlayer;
        if (player == null) return;
        Enqueue(new RaidEventPayload { Type = RaidEventType.Death, Player = NameOf(player), Level = player.Profile?.Info?.Level ?? 0, Map = world.LocationId, RaidTimeSeconds = Time.time - _raidStartedAt, Fields = new Dictionary<string, string> { ["Status"] = status.ToString(), ["Killer"] = player.KillerId ?? "Unknown", ["Weapon"] = WeaponName(player) }, Screenshot = Plugin.Screenshots.Value });
    }

    public void ReportExtract(ExitStatus status)
    {
        if (_raidEnded) return;
        _raidEnded = true;
        var world = Singleton<GameWorld>.Instance;
        var player = world?.MainPlayer;
        if (player == null) return;
        Enqueue(new RaidEventPayload { Type = RaidEventType.Extract, Player = NameOf(player), Level = player.Profile?.Info?.Level ?? 0, Map = world.LocationId, RaidTimeSeconds = Time.time - _raidStartedAt, Fields = new Dictionary<string, string> { ["Status"] = status.ToString() }, Screenshot = Plugin.Screenshots.Value });
        _wasInRaid = false;
    }

    public void ReportBossKill(Player boss, IPlayer aggressor, DamageInfo damageInfo, EBodyPart bodyPart)
    {
        if (!Plugin.BossKillEvents.Value || !ReferenceEquals(Singleton<GameWorld>.Instance?.MainPlayer, aggressor) || boss?.Profile == null) return;
        var world = Singleton<GameWorld>.Instance;
        Enqueue(new RaidEventPayload { Type = RaidEventType.BossKill, Player = NameOf((Player)aggressor), Level = ((Player)aggressor).Profile?.Info?.Level ?? 0, Map = world.LocationId, RaidTimeSeconds = Time.time - _raidStartedAt, Fields = new Dictionary<string, string> { ["Boss"] = boss.Profile.Info.Settings.Role.ToString(), ["Weapon"] = WeaponName((Player)aggressor), ["Body Part"] = bodyPart.ToString(), ["Headshot"] = bodyPart.ToString().IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ? "Yes" : "No", ["Distance"] = Vector3.Distance(boss.Position, ((Player)aggressor).Position).ToString("0") + "m" }, Screenshot = Plugin.Screenshots.Value });
    }

    public void ReportLoot(LootItem loot)
    {
        if (!Plugin.LootEvents.Value || loot?.Item == null || !_reportedLoot.Add(loot.Item.Id.ToString())) return;
        var world = Singleton<GameWorld>.Instance;
        var player = world?.MainPlayer;
        if (player == null) return;
        var item = loot.Item;
        var value = 0d;
        try { value = Singleton<Handbook>.Instance.GetBasePrice(item.TemplateId) * Math.Max(1, item.StackObjectsCount); } catch { }
        Enqueue(new RaidEventPayload { Type = RaidEventType.Loot, Player = NameOf(player), Level = player.Profile?.Info?.Level ?? 0, Map = world.LocationId, RaidTimeSeconds = Time.time - _raidStartedAt, Fields = new Dictionary<string, string> { ["Item"] = item.StringTemplateId, ["Quantity"] = item.StackObjectsCount.ToString(), ["Value"] = value.ToString("0") }, Screenshot = Plugin.Screenshots.Value });
    }

    public void ReportQuest(string questName, string trader)
    {
        if (!Plugin.QuestEvents.Value) return;
        var world = Singleton<GameWorld>.Instance;
        var player = world?.MainPlayer;
        if (player == null) return;
        Enqueue(new RaidEventPayload { Type = RaidEventType.Quest, Player = NameOf(player), Level = player.Profile?.Info?.Level ?? 0, Map = world.LocationId, RaidTimeSeconds = Time.time - _raidStartedAt, Fields = new Dictionary<string, string> { ["Quest"] = questName, ["Trader"] = trader }, Screenshot = Plugin.Screenshots.Value });
    }

    private void Enqueue(RaidEventPayload payload)
    {
        if (_pending.Count >= 128) return;
        _pending.Enqueue(payload);
        if (!_sending) { _sending = true; StartCoroutine(SendNext()); }
    }
    private IEnumerator SendNext()
    {
        var payload = _pending.Dequeue();
        if (payload.Screenshot) yield return CaptureScreenshot(payload);
        var json = JsonConvert.SerializeObject(payload, Formatting.None);
        using var request = new UnityWebRequest(Plugin.ServerUrl.Value.TrimEnd('/') + "/client/discordraidfeed/event", "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        var session = GetSessionId();
        if (!string.IsNullOrWhiteSpace(session)) request.SetRequestHeader("Cookie", "PHPSESSID=" + session);
        request.timeout = 10;
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) Plugin.Log.LogWarning("Event upload failed: " + request.error);
        _sending = false;
    }
    private IEnumerator CaptureScreenshot(RaidEventPayload payload)
    {
        var path = Path.Combine(Application.temporaryCachePath, "spt-discord-raid-feed.png");
        ScreenCapture.CaptureScreenshot(path);
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.15f);
        if (File.Exists(path))
        {
            try { payload.ScreenshotBase64 = Convert.ToBase64String(File.ReadAllBytes(path)); } catch (Exception ex) { Plugin.Log.LogWarning("Screenshot capture failed: " + ex.Message); }
        }
        payload.Screenshot = false;
    }
    private static string GetSessionId()
    {
        try { return Singleton<TarkovApplication>.Instance?.Session?.GetPhpSessionId(); } catch { return string.Empty; }
    }
    private static string NameOf(Player player) => player.Profile?.Info?.Nickname ?? "Unknown";
    private static string WeaponName(Player player) => player.HandsController?.Item?.ShortName ?? "Unknown";

    private sealed class RaidEventPayload
    {
        [JsonProperty("type")] public RaidEventType Type { get; set; }
        [JsonProperty("player")] public string Player { get; set; } = "Unknown";
        [JsonProperty("level")] public int Level { get; set; }
        [JsonProperty("map")] public string Map { get; set; } = "Unknown";
        [JsonProperty("raidTimeSeconds")] public double RaidTimeSeconds { get; set; }
        [JsonProperty("fields")] public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
        [JsonProperty("screenshotBase64")] public string ScreenshotBase64 { get; set; }
        [JsonIgnore] public bool Screenshot { get; set; }
    }
    private enum RaidEventType { Death, Extract, Loot, Quest, BossKill, LevelUp }
}
