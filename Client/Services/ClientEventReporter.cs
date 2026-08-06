using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HandBook;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;

namespace SPTDiscordReports.Client.Services;

public sealed class ClientEventReporter : MonoBehaviour
{
    public static ClientEventReporter? Instance { get; private set; }

    private readonly HashSet<string> _reportedLoot = new();
    private float _raidStartedAt;
    private int _lastLevel;
    private bool _inRaid;
    private bool _raidEnded;
    // Cached player data — updated every frame while raid is active, because Player/GameWorld
    // objects are destroyed by the time GameWorld.OnDestroy fires.
    private string _cachedPlayerName = "Unknown";
    private int _cachedLevel;
    private string _cachedMap = "Unknown";
    private bool _cachedIsAlive;
    private long _cachedFirValue;
    private long _cachedTotalValue;
    private string _cachedKillerName = "Unknown";
    private ExitStatus _cachedExitStatus = ExitStatus.Survived;
    // Delayed screenshot capture for extract/death — wait for SessionResultExitStatus UI + 1s
    private RaidEventPayload? _pendingRaidEndEvent;
    private float _raidEndTimeoutAt;
    private float _raidEndCaptureAt;

    private static readonly Dictionary<string, string> MapNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = "Customs",
        ["Woods"] = "Woods",
        ["Shoreline"] = "Shoreline",
        ["Interchange"] = "Interchange",
        ["RezervBase"] = "Reserve",
        ["laboratory"] = "The Lab",
        ["Lighthouse"] = "Lighthouse",
        ["TarkovStreets"] = "Streets of Tarkov",
        ["factory4_day"] = "Factory (Day)",
        ["factory4_night"] = "Factory (Night)",
        ["factory4"] = "Factory",
        ["Sandbox"] = "Ground Zero",
        ["Sandbox_high"] = "Ground Zero (High)",
        ["develop"] = "Develop",
    };

    private static string MapName(string? id) => string.IsNullOrWhiteSpace(id) ? "Unknown" : (MapNames.TryGetValue(id, out var n) ? n : id);

    public static ClientEventReporter? EnsureInstance()
    {
        if (Instance != null) return Instance;
        // Instance was lost (GameObject destroyed during scene transition). Create a new one.
        Plugin.Log.LogWarning("[DiscordRaidFeed] Instance was null, recreating ClientEventReporter GameObject");
        var host = new GameObject("SPTDiscordReportsClient_Restored");
        UnityEngine.Object.DontDestroyOnLoad(host);
        return host.AddComponent<ClientEventReporter>();
    }

    private void Awake()
    {
        Instance = this;
        Plugin.Log.LogInfo("[DiscordRaidFeed] ClientEventReporter Awake");
    }

    private void OnDestroy()
    {
        Plugin.Log.LogInfo("[DiscordRaidFeed] ClientEventReporter OnDestroy — Instance preserved for raid end events");
        // Don't clear Instance — we need it to survive during GameWorld.OnDestroy
    }

    private void Update()
    {
        try
        {
            if (!Plugin.Enabled.Value) return;

            // Cache player data every frame while raid is active.
            if (_inRaid)
            {
                var world = Singleton<GameWorld>.Instance;
                var player = world?.MainPlayer;
                if (world != null && player != null)
                {
                    _cachedPlayerName = NameOf(player);
                    _cachedLevel = player.Profile?.Info?.Level ?? 0;
                    _cachedMap = MapName(world.LocationId);
                    _cachedIsAlive = player.HealthController?.IsAlive ?? false;
                    _cachedFirValue = CalculateInventoryValue(player, true);
                    _cachedTotalValue = CalculateInventoryValue(player, false);

                    // Level-up detection
                    if (Plugin.LevelUpEvents.Value && _cachedLevel > _lastLevel)
                    {
                        _lastLevel = _cachedLevel;
                        Enqueue(new RaidEventPayload
                        {
                            Type = RaidEventType.LevelUp,
                            Player = _cachedPlayerName,
                            Level = _lastLevel,
                            Map = _cachedMap,
                            Fields = new Dictionary<string, string> { ["New Level"] = _lastLevel.ToString() }
                        });
                    }
                }
            }

            // Wait for SessionResultExitStatus.Show to fire + 1s delay before capturing raid-end screenshot
            if (_pendingRaidEndEvent != null)
            {
                if (_raidEndCaptureAt > 0 && Time.time >= _raidEndCaptureAt)
                {
                    var evt = _pendingRaidEndEvent;
                    _pendingRaidEndEvent = null;
                    Plugin.Log.LogInfo($"[DiscordRaidFeed] Capturing screenshot for {evt.Type} (1s after UI shown)");
                    Enqueue(evt);
                }
                else if (Time.time >= _raidEndTimeoutAt)
                {
                    var evt = _pendingRaidEndEvent;
                    _pendingRaidEndEvent = null;
                    Plugin.Log.LogWarning($"[DiscordRaidFeed] Timeout waiting for SessionResultExitStatus, sending {evt.Type} anyway");
                    Enqueue(evt);
                }
            }

            // Events are sent immediately via background thread in Enqueue()
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] Update error: {ex}"); }
    }

    public void OnRaidStart(GameWorld world)
    {
        _inRaid = true;
        _raidEnded = false;
        _raidStartedAt = Time.time;
        _reportedLoot.Clear();

        var player = world.MainPlayer;
        _cachedPlayerName = NameOf(player);
        _cachedLevel = player?.Profile?.Info?.Level ?? 0;
        _cachedMap = MapName(world.LocationId);
        _cachedIsAlive = true;
        _cachedFirValue = 0;
        _cachedTotalValue = 0;
        _cachedKillerName = "Unknown";
        _lastLevel = _cachedLevel;

        Plugin.Log.LogInfo($"[DiscordRaidFeed] Raid started: map={_cachedMap}, player={_cachedPlayerName}");
    }

    public void OnPlayerDeath(Player victim)
    {
        _cachedIsAlive = false;
        _cachedKillerName = ResolveKillerName(victim);
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Player death detected: killer={_cachedKillerName}");
    }

    public void OnRaidStop(ExitStatus exitStatus)
    {
        _cachedExitStatus = exitStatus;
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Raid stop detected: exitStatus={exitStatus}");

        // Update the pending raid-end event with the correct exit status
        if (_pendingRaidEndEvent != null)
        {
            // Redetermine event type based on exit status
            if (!_cachedIsAlive)
            {
                _pendingRaidEndEvent.Type = RaidEventType.Death;
            }
            else if (exitStatus == ExitStatus.Runner)
            {
                _pendingRaidEndEvent.Type = RaidEventType.RunThrough;
            }
            else
            {
                _pendingRaidEndEvent.Type = RaidEventType.Extract;
            }
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Updated pending event type to {_pendingRaidEndEvent.Type}");

            // Set 1s delay for screenshot capture (let the UI fully render)
            _raidEndCaptureAt = Time.time + 1f;
        }
    }

    public void OnRaidEnd()
    {
        Plugin.Log.LogInfo($"[DiscordRaidFeed] OnRaidEnd called: _inRaid={_inRaid}, _raidEnded={_raidEnded}, player={_cachedPlayerName}, isAlive={_cachedIsAlive}, exitStatus={_cachedExitStatus}");

        if (!_inRaid || _raidEnded) return;
        _raidEnded = true;

        var raidTime = Time.time - _raidStartedAt;

        // Determine event type based on exit status and alive state
        RaidEventType eventType;
        Dictionary<string, string> fields;
        if (!_cachedIsAlive)
        {
            eventType = RaidEventType.Death;
            fields = new Dictionary<string, string>
            {
                ["Killer"] = _cachedKillerName,
                ["Gear Value Lost"] = FormatValue(_cachedTotalValue),
            };
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Reporting death: killer={_cachedKillerName}, gearValue={_cachedTotalValue}, raidTime={raidTime}s");
        }
        else if (_cachedExitStatus == ExitStatus.Runner)
        {
            eventType = RaidEventType.RunThrough;
            fields = new Dictionary<string, string>
            {
                ["FIR Loot Value"] = FormatValue(_cachedFirValue),
                ["Total Inventory Value"] = FormatValue(_cachedTotalValue),
            };
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Reporting run-through: firValue={_cachedFirValue}, totalValue={_cachedTotalValue}, raidTime={raidTime}s");
        }
        else
        {
            eventType = RaidEventType.Extract;
            fields = new Dictionary<string, string>
            {
                ["FIR Loot Value"] = FormatValue(_cachedFirValue),
                ["Total Inventory Value"] = FormatValue(_cachedTotalValue),
            };
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Reporting extract: firValue={_cachedFirValue}, totalValue={_cachedTotalValue}, raidTime={raidTime}s");
        }

        // Delay screenshot capture for extract/death/run-through — capture after scene transition
        // so we get the raid ended screen instead of the loading screen
        _pendingRaidEndEvent = new RaidEventPayload
        {
            Type = eventType,
            Player = _cachedPlayerName,
            Level = _cachedLevel,
            Map = _cachedMap,
            RaidTimeSeconds = raidTime,
            Fields = fields,
            Screenshot = Plugin.Screenshots.Value
        };
        _raidEndTimeoutAt = Time.time + 20f; // 20s timeout fallback if SessionResultExitStatus never shows
        _raidEndCaptureAt = 0f; // Will be set by OnRaidStop when SessionResultExitStatus.Show fires
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Raid end event queued, waiting for SessionResultExitStatus.Show (20s timeout)");

        _inRaid = false;
    }

    public void ReportBossKill(Player boss, IPlayer aggressor, DamageInfo damageInfo, EBodyPart bodyPart)
    {
        if (!Plugin.BossKillEvents.Value) return;
        if (!ReferenceEquals(Singleton<GameWorld>.Instance?.MainPlayer, aggressor)) return;
        if (boss?.Profile == null) return;

        var killer = (Player)aggressor;
        var weapon = damageInfo.Weapon?.Name ?? "?";
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Boss kill: boss={boss.Profile.Info.Settings.Role}, weapon={weapon}");
        Enqueue(new RaidEventPayload
        {
            Type = RaidEventType.BossKill,
            Player = NameOf(killer),
            Level = killer.Profile?.Info?.Level ?? 0,
            Map = _cachedMap,
            RaidTimeSeconds = Time.time - _raidStartedAt,
            Fields = new Dictionary<string, string>
            {
                ["Boss"] = boss.Profile.Info.Settings.Role.ToString(),
                ["Weapon"] = weapon,
                ["Body Part"] = bodyPart.ToString(),
                ["Headshot"] = bodyPart.ToString().IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ? "Yes" : "No",
                ["Distance"] = Vector3.Distance(boss.Position, killer.Position).ToString("0") + "m"
            },
            Screenshot = Plugin.Screenshots.Value
        });
    }

    public void ReportLoot(Item item)
    {
        if (!Plugin.LootEvents.Value || item == null) return;
        if (!_reportedLoot.Add(item.Id.ToString())) return;

        var itemName = item.LocalizedName();
        var value = 0d;
        try { value = Singleton<Handbook>.Instance.GetBasePrice(item.TemplateId) * Math.Max(1, item.StackObjectsCount); } catch { }
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Loot picked up: item={itemName}, value={value}");
        Enqueue(new RaidEventPayload
        {
            Type = RaidEventType.Loot,
            Player = _cachedPlayerName,
            Level = _cachedLevel,
            Map = _cachedMap,
            RaidTimeSeconds = Time.time - _raidStartedAt,
            Fields = new Dictionary<string, string>
            {
                ["Item"] = itemName,
                ["Quantity"] = item.StackObjectsCount.ToString(),
                ["Value"] = FormatValue((long)value),
            },
            Screenshot = Plugin.Screenshots.Value
        });
    }

    public void ReportQuest(string questName, string trader)
    {
        if (!Plugin.QuestEvents.Value) return;
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Quest completed: {questName}");
        Enqueue(new RaidEventPayload
        {
            Type = RaidEventType.Quest,
            Player = _cachedPlayerName,
            Level = _cachedLevel,
            Map = _cachedMap,
            RaidTimeSeconds = Time.time - _raidStartedAt,
            Fields = new Dictionary<string, string> { ["Quest"] = questName, ["Trader"] = trader },
            Screenshot = Plugin.Screenshots.Value
        });
    }

    private string ResolveKillerName(Player victim)
    {
        try
        {
            var killerId = victim.KillerId;
            if (string.IsNullOrWhiteSpace(killerId)) return "Unknown";
            var world = Singleton<GameWorld>.Instance;
            if (world != null)
            {
                var killer = world.GetAlivePlayerByProfileID(killerId);
                if (killer != null)
                {
                    var name = killer.Profile?.Info?.Nickname ?? killer.Profile?.Nickname ?? "Unknown";
                    _cachedKillerName = name;
                    return name;
                }
                var dead = world.AllPlayersEverExisted?.FirstOrDefault(p => p.ProfileId == killerId);
                if (dead != null)
                {
                    var name = dead.Profile?.Info?.Nickname ?? dead.Profile?.Nickname ?? "Unknown";
                    _cachedKillerName = name;
                    return name;
                }
            }
            _cachedKillerName = killerId;
            return killerId;
        }
        catch { return "Unknown"; }
    }

    private static long CalculateInventoryValue(Player player, bool firOnly)
    {
        try
        {
            var inventory = player.Inventory;
            if (inventory == null) return 0;
            var handbook = Singleton<Handbook>.Instance;
            long total = 0;
            foreach (var item in inventory.AllRealPlayerItems)
            {
                if (item == null) continue;
                if (firOnly && !item.SpawnedInSession) continue;
                try { total += (long)(handbook.GetBasePrice(item.TemplateId) * Math.Max(1, item.StackObjectsCount)); } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private static string FormatValue(long value) => value >= 1000000 ? $"{value / 1000000.0:0.##}M ₽" : value >= 1000 ? $"{value / 1000.0:0.#}k ₽" : $"{value} ₽";

    private void Enqueue(RaidEventPayload payload)
    {
        // Capture screenshot to file (async, no main-thread stutter) before sending
        if (payload.Screenshot)
        {
            payload.ScreenshotPath = CaptureScreenshotToFile();
            payload.Screenshot = false;
        }
        // Send immediately on a background thread — don't rely on Update/coroutine
        // which may not run during scene transitions after GameWorld.OnDestroy
        Task.Run(() => SendEventAsync(payload));
    }

    private static string? CaptureScreenshotToFile()
    {
        try
        {
            var path = Path.Combine(Application.temporaryCachePath, $"spt-discord-raid-feed-{Guid.NewGuid():N}.png");
            ScreenCapture.CaptureScreenshot(path);
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Screenshot capturing to file: {path}");
            return path;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[DiscordRaidFeed] Screenshot capture failed: {ex.Message}");
            return null;
        }
    }

    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    private static async Task SendEventAsync(RaidEventPayload payload)
    {
        try
        {
            // If screenshot was captured to file, wait for it to be written then read+encode on background thread
            if (!string.IsNullOrEmpty(payload.ScreenshotPath))
            {
                // ScreenCapture.CaptureScreenshot writes asynchronously — wait for the file
                for (var i = 0; i < 30; i++)
                {
                    if (File.Exists(payload.ScreenshotPath) && new FileInfo(payload.ScreenshotPath).Length > 0)
                        break;
                    await Task.Delay(100);
                }
                try
                {
                    if (File.Exists(payload.ScreenshotPath))
                    {
                        var bytes = File.ReadAllBytes(payload.ScreenshotPath);
                        payload.ScreenshotBase64 = Convert.ToBase64String(bytes);
                        Plugin.Log.LogInfo($"[DiscordRaidFeed] Screenshot read: {bytes.Length} bytes, base64 length={payload.ScreenshotBase64.Length}");
                        try { File.Delete(payload.ScreenshotPath); } catch { }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[DiscordRaidFeed] Screenshot read failed: {ex.Message}"); }
                payload.ScreenshotPath = null;
            }

            var json = JsonConvert.SerializeObject(payload, Formatting.None);
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Sending event to server: {payload.Type}, json length={json.Length}");

            var url = Plugin.ServerUrl.Value.TrimEnd('/') + "/client/discordraidfeed/event";
            var session = GetSessionId();

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrWhiteSpace(session))
                client.DefaultRequestHeaders.Add("Cookie", "PHPSESSID=" + session);
            // Tell SPT server the body is NOT compressed (otherwise it tries ZLibStream decompression)
            client.DefaultRequestHeaders.Add("requestcompressed", "0");

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                Plugin.Log.LogInfo($"[DiscordRaidFeed] Event sent to server: {payload.Type} (response: {body})");
            else
                Plugin.Log.LogWarning($"[DiscordRaidFeed] Event upload failed: {(int)response.StatusCode} {response.StatusCode} - {body}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[DiscordRaidFeed] SendEventAsync error: {ex}"); }
    }

    private static string GetSessionId()
    {
        try { return Singleton<TarkovApplication>.Instance?.Session?.GetPhpSessionId(); } catch { return string.Empty; }
    }

    private static string NameOf(Player? player) => player?.Profile?.Info?.Nickname ?? player?.Profile?.Nickname ?? "Unknown";

    private sealed class RaidEventPayload
    {
        [JsonProperty("type")] public RaidEventType Type { get; set; }
        [JsonProperty("player")] public string Player { get; set; } = "Unknown";
        [JsonProperty("level")] public int Level { get; set; }
        [JsonProperty("map")] public string Map { get; set; } = "Unknown";
        [JsonProperty("raidTimeSeconds")] public double RaidTimeSeconds { get; set; }
        [JsonProperty("fields")] public Dictionary<string, string> Fields { get; set; } = new();
        [JsonProperty("screenshotBase64")] public string? ScreenshotBase64 { get; set; }
        [JsonIgnore] public bool Screenshot { get; set; }
        [JsonIgnore] public string? ScreenshotPath { get; set; }
    }

    private enum RaidEventType { Death, Extract, RunThrough, Loot, Quest, BossKill, LevelUp }
}
