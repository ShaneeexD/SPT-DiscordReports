namespace DiscordRaidFeed.Server.Events;

public enum RaidEventType { Death, Extract, RunThrough, Loot, Quest, BossKill, LevelUp }

public sealed record RaidEvent
{
    public RaidEventType Type { get; init; }
    public string Player { get; init; } = "Unknown";
    public int Level { get; init; }
    public string Map { get; init; } = "Unknown";
    public double RaidTimeSeconds { get; init; }
    public string? ScreenshotPath { get; init; }
    public string? ScreenshotBase64 { get; init; }
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
