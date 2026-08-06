using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTDiscordReports.Server.Config;
using SPTDiscordReports.Server.Utils;

namespace SPTDiscordReports.Server.Services;

[Injectable(InjectionType.Singleton)]
public sealed class ConfigService(Log log)
{
    private const int SupportedConfigVersion = 1;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, RemoteState> _states = new(StringComparer.OrdinalIgnoreCase);
    private string _modPath = string.Empty;
    public LocalConfig Local { get; private set; } = new();

    public void Initialise(string modPath)
    {
        _modPath = modPath;
        Directory.CreateDirectory(Path.Combine(modPath, "config", "cache"));
        var path = Path.Combine(modPath, "config", "config.json");
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(Local, JsonOptions.Default));
        Local = JsonSerializer.Deserialize<LocalConfig>(File.ReadAllText(path), JsonOptions.Default) ?? new();
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        foreach (var destination in Local.Webhooks.Where(x => Uri.TryCreate(x.ConfigUrl, UriKind.Absolute, out _)))
            await RefreshOneAsync(destination, token).ConfigureAwait(false);
    }

    public RemoteConfig Get(WebhookDestination destination) => _states.TryGetValue(destination.Name, out var state) ? state.Config : new();
    public bool IsScreenshotEnabled(WebhookDestination destination, Events.RaidEventType type)
    {
        var s = Get(destination).Settings.Screenshots;
        return s.Enabled && type switch { Events.RaidEventType.Death => s.DeathScreenshots, Events.RaidEventType.Extract => s.ExtractScreenshots, Events.RaidEventType.RunThrough => s.ExtractScreenshots, Events.RaidEventType.Loot => s.RareLootScreenshots, Events.RaidEventType.Quest => s.QuestScreenshots, Events.RaidEventType.BossKill => s.BossKillScreenshots, _ => false };
    }

    public bool IsEnabled(WebhookDestination destination, Events.RaidEventType type, Events.RaidEvent e)
    {
        var s = Get(destination).Settings;
        if (s.Filters.MinimumRaidDuration > 0 && e.RaidTimeSeconds > 0 && e.RaidTimeSeconds < s.Filters.MinimumRaidDuration)
        {
            log.Info($"Event {type} filtered: raidTime {e.RaidTimeSeconds}s < minimum {s.Filters.MinimumRaidDuration}s for {destination.Name}");
            return false;
        }
        if (s.Filters.IgnoredMaps.Any(x => string.Equals(x, e.Map, StringComparison.OrdinalIgnoreCase)))
        {
            log.Info($"Event {type} filtered: map {e.Map} in ignoredMaps for {destination.Name}");
            return false;
        }
        var enabled = type switch { Events.RaidEventType.Death => s.Events.Deaths, Events.RaidEventType.Extract => s.Events.Extracts, Events.RaidEventType.RunThrough => s.Events.RunThroughs, Events.RaidEventType.Loot => s.Events.Loot, Events.RaidEventType.Quest => s.Events.Quests, Events.RaidEventType.BossKill => s.Events.BossKills, Events.RaidEventType.LevelUp => s.Events.LevelUps, _ => false };
        if (!enabled) log.Info($"Event {type} filtered: disabled in remote config for {destination.Name}");
        return enabled;
    }

    private async Task RefreshOneAsync(WebhookDestination destination, CancellationToken token)
    {
        var cache = Path.Combine(_modPath, "config", "cache", $"{Sanitise(destination.Name)}.json");
        try
        {
            var json = await _http.GetStringAsync(destination.ConfigUrl, token).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            if (_states.TryGetValue(destination.Name, out var old) && old.Hash == hash) { old.LastCheckedUtc = DateTimeOffset.UtcNow; return; }
            var config = JsonSerializer.Deserialize<RemoteConfig>(json, JsonOptions.Default) ?? throw new InvalidDataException("Empty configuration");
            if (config.ConfigVersion <= 0 || config.ConfigVersion > SupportedConfigVersion) throw new InvalidDataException($"Unsupported configVersion {config.ConfigVersion}");
            if (!Version.TryParse(config.MinimumModVersion, out var minimum) || minimum > new Version("1.0.0")) log.Warning($"{destination.Name} requires mod version {config.MinimumModVersion}.");
            File.WriteAllText(cache, json);
            _states[destination.Name] = new RemoteState(config, hash, DateTimeOffset.UtcNow, null);
            log.Info($"Updated remote configuration for {destination.Name}.");
        }
        catch (Exception ex)
        {
            log.Warning($"Remote configuration unavailable for {destination.Name}; using cache if available. {ex.Message}");
            if (!_states.ContainsKey(destination.Name) && File.Exists(cache))
            {
                try { var json = File.ReadAllText(cache); var config = JsonSerializer.Deserialize<RemoteConfig>(json, JsonOptions.Default); if (config is not null) _states[destination.Name] = new(config, "cached", DateTimeOffset.UtcNow, ex.Message); } catch (Exception cacheEx) { log.Error("Unable to read remote configuration cache.", cacheEx); }
            }
        }
    }
    private static string Sanitise(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
    private sealed class RemoteState(RemoteConfig config, string hash, DateTimeOffset lastCheckedUtc, string? lastError)
    {
        public RemoteConfig Config { get; } = config;
        public string Hash { get; } = hash;
        public DateTimeOffset LastCheckedUtc { get; set; } = lastCheckedUtc;
        public string? LastError { get; } = lastError;
    }
}

internal static class JsonOptions { public static readonly JsonSerializerOptions Default = new() { PropertyNameCaseInsensitive = true, WriteIndented = true }; }
