using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace DiscordRaidFeed.Server.Events;

public sealed class RaidEventRequest : IRequestData
{
    [JsonPropertyName("type")] public RaidEventType Type { get; set; }
    [JsonPropertyName("player")] public string Player { get; set; } = "Unknown";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("map")] public string Map { get; set; } = "Unknown";
    [JsonPropertyName("raidTimeSeconds")] public double RaidTimeSeconds { get; set; }
    [JsonPropertyName("screenshotPath")] public string? ScreenshotPath { get; set; }
    [JsonPropertyName("screenshotBase64")] public string? ScreenshotBase64 { get; set; }
    [JsonPropertyName("fields")] public Dictionary<string, string> Fields { get; set; } = new();
    public RaidEvent ToEvent() => new() { Type = Type, Player = Player, Level = Level, Map = Map, RaidTimeSeconds = RaidTimeSeconds, ScreenshotPath = ScreenshotPath, ScreenshotBase64 = ScreenshotBase64, Fields = Fields };
}
