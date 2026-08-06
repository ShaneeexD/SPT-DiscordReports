using SPTDiscordReports.Server.Events;
using SPTDiscordReports.Server.Utils;

namespace SPTDiscordReports.Server.Discord;

public static class EmbedBuilder
{
    public static DiscordEmbed Build(RaidEvent e)
    {
        var (title, color) = e.Type switch
        {
            RaidEventType.Death => ("☠ Operator Down", 0xD32F2F),
            RaidEventType.Extract => ("✅ Successful Extraction", 0x43A047),
            RaidEventType.Loot => ("💎 Rare Loot Found", 0x8E44AD),
            RaidEventType.Quest => ("📋 Quest Complete", 0x1976D2),
            RaidEventType.BossKill => ("👑 Boss Eliminated", 0xF9A825),
            RaidEventType.LevelUp => ("⬆ Level Up", 0x00897B),
            _ => ("Raid Event", 0x607D8B)
        };
        var embed = new DiscordEmbed { Title = title, Color = color };
        embed.Fields.Add(new() { Name = "Player", Value = e.Player });
        if (e.Level > 0) embed.Fields.Add(new() { Name = "Level", Value = e.Level.ToString() });
        var mapName = MapNames.Resolve(e.Map);
        if (!string.IsNullOrWhiteSpace(mapName) && mapName != "Unknown") embed.Fields.Add(new() { Name = "Map", Value = mapName });
        if (e.RaidTimeSeconds > 0) embed.Fields.Add(new() { Name = "Raid Time", Value = TimeSpan.FromSeconds(e.RaidTimeSeconds).ToString(@"mm\:ss") });
        foreach (var field in e.Fields) embed.Fields.Add(new() { Name = field.Key, Value = field.Value });
        return embed;
    }
}
