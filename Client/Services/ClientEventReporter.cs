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

namespace DiscordRaidFeed.Client.Services;

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
    private string _cachedProfileId = "";
    private int _cachedLevel;
    private string _cachedMap = "Unknown";
    private bool _cachedIsAlive;
    private long _cachedFirValue;
    private long _cachedTotalValue;
    private long _cachedLostOnDeathValue;
    private string _cachedKillerName = "Unknown";
    private ExitStatus _cachedExitStatus = ExitStatus.Survived;
    // Delayed screenshot capture for extract/death — wait for SessionResultExitStatus UI + 1s
    private RaidEventPayload? _pendingRaidEndEvent;
    private float _raidEndTimeoutAt;
    private float _raidEndCaptureAt;
    // Delayed screenshot for achievements — wait 1s for the notification UI to appear
    private readonly Queue<(RaidEventPayload payload, float captureAt)> _pendingAchievementEvents = new();

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

    // Maps WildSpawnType enum names (boss.Profile.Info.Settings.Role.ToString()) to friendly names.
    private static readonly Dictionary<string, string> BossNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Bosses
        ["bossBully"] = "Reshala",
        ["bossGluhar"] = "Glukhar",
        ["bossKilla"] = "Killa",
        ["bossKojaniy"] = "Shturman",
        ["bossSanitar"] = "Sanitar",
        ["bossTagilla"] = "Tagilla",
        ["bossKnight"] = "Knight",
        ["bossZryachiy"] = "Zryachiy",
        ["bossBoar"] = "Kaban",
        ["bossBoarSniper"] = "Kaban Sniper",
        ["bossKolontay"] = "Kolontay",
        ["bossPartisan"] = "Partisan",
        // Followers / guards
        ["followerBully"] = "Reshala Guard",
        ["followerKojaniy"] = "Shturman Guard",
        ["followerSanitar"] = "Sanitar Guard",
        ["followerTagilla"] = "Tagilla Guard",
        ["followerGluharAssault"] = "Glukhar Guard (Assault)",
        ["followerGluharSecurity"] = "Glukhar Guard (Security)",
        ["followerGluharScout"] = "Glukhar Guard (Scout)",
        ["followerGluharSnipe"] = "Glukhar Guard (Sniper)",
        ["followerBigPipe"] = "Big Pipe",
        ["followerBirdEye"] = "Bird Eye",
        ["followerZryachiy"] = "Zryachiy Guard",
        ["followerBoar"] = "Kaban Guard",
        ["followerBoarClose1"] = "Kaban Guard",
        ["followerBoarClose2"] = "Kaban Guard",
        ["followerKolontayAssault"] = "Kolontay Guard (Assault)",
        ["followerKolontaySecurity"] = "Kolontay Guard (Security)",
    };

    private static string BossName(string? role) => string.IsNullOrWhiteSpace(role) ? "Unknown" : (BossNames.TryGetValue(role, out var n) ? n : role!);

    // Maps scav / other AI WildSpawnType roles to friendly English names.
    private static readonly Dictionary<string, string> ScavNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assault"] = "Scav",
        ["assaultGroup"] = "Scav Group",
        ["marksman"] = "Scav Sniper",
        ["cursedAssault"] = "Cursed Scav",
        ["crazyAssaultEvent"] = "Crazy Scav",
        ["pmcBot"] = "Raider",
        ["exUsec"] = "Rogue",
        ["sectantPriest"] = "Cultist Priest",
        ["sectantWarrior"] = "Cultist",
        ["arenaFighterEvent"] = "Bloodhound",
        ["shooterBTR"] = "BTR",
    };

    private static string ScavName(string? role) => string.IsNullOrWhiteSpace(role) ? "Unknown" : (ScavNames.TryGetValue(role, out var n) ? n : role!);

    public static ClientEventReporter? EnsureInstance()
    {
        if (Instance != null) return Instance;
        // Instance was lost (GameObject destroyed during scene transition). Create a new one.
        Plugin.Log.LogWarning("[DiscordRaidFeed] Instance was null, recreating ClientEventReporter GameObject");
        var host = new GameObject("DiscordRaidFeedClient_Restored");
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
                    CalculateInventoryValues(player, out _cachedFirValue, out _cachedTotalValue, out _cachedLostOnDeathValue);

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

            // Process pending achievement events — capture screenshot after 1s delay
            while (_pendingAchievementEvents.Count > 0 && Time.time >= _pendingAchievementEvents.Peek().captureAt)
            {
                var (evt, _) = _pendingAchievementEvents.Dequeue();
                Plugin.Log.LogInfo($"[DiscordRaidFeed] Capturing screenshot for Achievement (1s after unlock)");
                Enqueue(evt);
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
        _cachedProfileId = player?.ProfileId ?? "";
        _cachedLevel = player?.Profile?.Info?.Level ?? 0;
        _cachedMap = MapName(world.LocationId);
        _cachedIsAlive = true;
        _cachedFirValue = 0;
        _cachedTotalValue = 0;
        _cachedLostOnDeathValue = 0;
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
                ["Gear Value Lost"] = FormatValue(_cachedLostOnDeathValue),
            };
            Plugin.Log.LogInfo($"[DiscordRaidFeed] Reporting death: killer={_cachedKillerName}, gearValue={_cachedLostOnDeathValue}, raidTime={raidTime}s");
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
        var weapon = damageInfo.Weapon?.LocalizedName() ?? "?";
        var role = boss.Profile.Info.Settings.Role.ToString();
        var bossName = BossName(role);
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Boss kill: boss={role} ({bossName}), weapon={weapon}");
        Enqueue(new RaidEventPayload
        {
            Type = RaidEventType.BossKill,
            Player = NameOf(killer),
            Level = killer.Profile?.Info?.Level ?? 0,
            Map = _cachedMap,
            RaidTimeSeconds = Time.time - _raidStartedAt,
            Fields = new Dictionary<string, string>
            {
                ["Boss"] = bossName,
                ["Weapon"] = weapon,
                ["Body Part"] = bodyPart.ToString(),
                ["Headshot"] = bodyPart.ToString().IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ? "Yes" : "No",
                ["Distance"] = Vector3.Distance(boss.Position, killer.Position).ToString("0") + "m"
            },
            Screenshot = Plugin.Screenshots.Value
        });
    }

    public string? CaptureLootScreenshot()
    {
        if (!Plugin.Screenshots.Value) return null;
        return CaptureScreenshotToFile();
    }

    public void ReportLoot(Item item, string? screenshotPath = null)
    {
        if (!Plugin.LootEvents.Value || item == null) return;
        if (!_reportedLoot.Add(item.Id.ToString())) return;

        var itemName = item.LocalizedName();
        // Sum the value of the item plus all its children (attachments, mods, contents)
        var value = ItemTreeValue(item);
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Loot picked up: item={itemName}, value={value} (incl. children)");
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
                ["ValueRaw"] = ((long)value).ToString(),
            },
            Screenshot = false, // Already captured in prefix
            ScreenshotPath = screenshotPath
        });
    }

    public void ReportQuest(string questName, string trader)
    {
        if (!Plugin.QuestEvents.Value) return;
        // Quests can be handed in outside raid, so resolve the player name from the cached profile
        var playerName = _cachedPlayerName;
        try
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player?.Profile?.Info?.Nickname != null)
            {
                playerName = player.Profile.Info.Nickname;
            }
            else
            {
                // In SPT the session ID is the profile ID — look up the cached nickname
                var sessionId = GetSessionId();
                Plugin.Log.LogInfo($"[DiscordRaidFeed] Quest name lookup: sessionId={sessionId ?? "null"}, cachedNames={Plugin.ProfileNicknames.Count}");
                if (!string.IsNullOrWhiteSpace(sessionId) && Plugin.ProfileNicknames.TryGetValue(sessionId, out var nick))
                    playerName = nick;
                else if (Plugin.ProfileNicknames.Count > 0)
                    playerName = Plugin.ProfileNicknames.First().Value;
            }
        }
        catch { }
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Quest completed: {questName}, player={playerName}");
        Enqueue(new RaidEventPayload
        {
            Type = RaidEventType.Quest,
            Player = playerName,
            Level = _cachedLevel,
            Map = _cachedMap,
            Fields = new Dictionary<string, string> { ["Quest"] = questName, ["Trader"] = trader },
            Screenshot = Plugin.Screenshots.Value
        });
    }

    public void ReportAchievement(string achievementName, string rarity)
    {
        if (!Plugin.AchievementEvents.Value) return;
        // Achievements can unlock outside raid, so resolve the player name from the cached profile
        var playerName = _cachedPlayerName;
        try
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player?.Profile?.Info?.Nickname != null)
                playerName = player.Profile.Info.Nickname;
            else
            {
                var sessionId = GetSessionId();
                if (!string.IsNullOrWhiteSpace(sessionId) && Plugin.ProfileNicknames.TryGetValue(sessionId, out var nick))
                    playerName = nick;
                else if (Plugin.ProfileNicknames.Count > 0)
                    playerName = Plugin.ProfileNicknames.First().Value;
            }
        }
        catch { }
        Plugin.Log.LogInfo($"[DiscordRaidFeed] Achievement unlocked: {achievementName}, player={playerName}");
        // Delay screenshot by 1s so the achievement notification UI is visible
        _pendingAchievementEvents.Enqueue((new RaidEventPayload
        {
            Type = RaidEventType.Achievement,
            Player = playerName,
            Level = _cachedLevel,
            Map = _cachedMap,
            Fields = new Dictionary<string, string> { ["Achievement"] = achievementName, ["Rarity"] = rarity },
            Screenshot = Plugin.Screenshots.Value
        }, Time.time + 1f));
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
                    var name = KillerDisplayName(killer);
                    _cachedKillerName = name;
                    return name;
                }
                var dead = world.AllPlayersEverExisted?.FirstOrDefault(p => p.ProfileId == killerId);
                if (dead != null)
                {
                    var name = KillerDisplayName(dead);
                    _cachedKillerName = name;
                    return name;
                }
            }
            _cachedKillerName = killerId;
            return killerId;
        }
        catch { return "Unknown"; }
    }

    // For bosses/followers/scavs and other AI, use the friendly English role name
    // instead of the localized Russian Nickname. For PMCs and real players, fall back to Nickname.
    private static string KillerDisplayName(Player killer)
    {
        try
        {
            var role = killer.Profile?.Info?.Settings?.Role.ToString();
            if (!string.IsNullOrWhiteSpace(role))
            {
                if (role.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    role.IndexOf("follower", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return BossName(role);
                }
                if (ScavNames.ContainsKey(role))
                {
                    return ScavName(role);
                }
            }
        }
        catch { }
        return killer.Profile?.Info?.Nickname ?? killer.Profile?.Nickname ?? "Unknown";
    }

    // Equipment slots whose contents are NOT lost on death.
    private static readonly HashSet<string> KeptOnDeathSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "SecuredContainer", // secure container + everything inside it
        "Scabbard",         // melee weapon
        "SpecialSlot",      // compass, wi-fi camera, etc.
    };

    // Walk up the parent chain to determine if an item lives in a slot that is kept on death.
    // Guarded against cycles with a visited set and a depth cap.
    private static bool IsKeptOnDeath(Item item)
    {
        try
        {
            var current = item;
            int depth = 0;
            while (current != null && depth < 32)
            {
                var container = current.Parent?.Container;
                if (container == null) break;
                if (KeptOnDeathSlots.Contains(container.ID))
                    return true;
                current = container.ParentItem;
                depth++;
            }
        }
        catch { }
        return false;
    }

    // Single pass over AllRealPlayerItems computing all three value variants at once,
    // so we only iterate the inventory once per frame instead of three times.
    private static void CalculateInventoryValues(Player player, out long firValue, out long totalValue, out long lostOnDeathValue)
    {
        firValue = 0;
        totalValue = 0;
        lostOnDeathValue = 0;
        try
        {
            var inventory = player.Inventory;
            if (inventory == null) return;
            var handbook = Singleton<Handbook>.Instance;
            foreach (var item in inventory.AllRealPlayerItems)
            {
                if (item == null) continue;
                long itemValue = 0;
                try { itemValue = (long)(handbook.GetBasePrice(item.TemplateId) * Math.Max(1, item.StackObjectsCount)); } catch { }
                totalValue += itemValue;
                if (item.SpawnedInSession) firValue += itemValue;
                if (!IsKeptOnDeath(item)) lostOnDeathValue += itemValue;
            }
        }
        catch { }
    }

    private static string FormatValue(long value) => value >= 1000000 ? $"{value / 1000000.0:0.##}M ₽" : value >= 1000 ? $"{value / 1000.0:0.#}k ₽" : $"{value} ₽";

    // Sums the handbook price of an item and all its children (attachments, mods, contained items).
    private static double ItemTreeValue(Item item)
    {
        try
        {
            var handbook = Singleton<Handbook>.Instance;
            var total = 0d;
            foreach (var it in item.GetAllItems())
            {
                if (it == null) continue;
                try { total += handbook.GetBasePrice(it.TemplateId) * Math.Max(1, it.StackObjectsCount); } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private void Enqueue(RaidEventPayload payload)
    {
        // Skip posting for SPT Developer profiles (unless username is "Dev2")
        if (Plugin.DevProfileIds.Contains(_cachedProfileId))
        {
            Plugin.Log.LogDebug($"[DiscordRaidFeed] Skipping event {payload.Type} for dev profile {_cachedProfileId}");
            return;
        }

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
            var path = Path.Combine(Application.temporaryCachePath, $"discord-raid-feed-{Guid.NewGuid():N}.png");
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

    private enum RaidEventType { Death, Extract, RunThrough, Loot, Quest, BossKill, LevelUp, Achievement }
}
