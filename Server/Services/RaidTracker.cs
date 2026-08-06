using SPTarkov.DI.Annotations;

namespace DiscordRaidFeed.Server.Services;

[Injectable(InjectionType.Singleton)]
public sealed class RaidTracker
{
    private readonly Dictionary<string, string> _raidMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _raidStartsUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public void OnRaidStart(string sessionId, string location)
    {
        lock (_lock)
        {
            _raidMaps[sessionId] = location;
            _raidStartsUtc[sessionId] = DateTime.UtcNow;
        }
    }

    public (string map, DateTime startedAt) OnRaidEnd(string sessionId)
    {
        lock (_lock)
        {
            _raidMaps.TryGetValue(sessionId, out var map);
            _raidMaps.Remove(sessionId);
            var startedAt = _raidStartsUtc.TryGetValue(sessionId, out var s) ? s : default;
            _raidStartsUtc.Remove(sessionId);
            return (map ?? "Unknown", startedAt);
        }
    }
}
