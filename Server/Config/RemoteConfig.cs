namespace SPTDiscordReports.Server.Config;

public sealed class RemoteConfig
{
    public int ConfigVersion { get; set; }
    public string MinimumModVersion { get; set; } = "1.0.0";
    public string CommunityName { get; set; } = "Discord Community";
    public RemoteSettings Settings { get; set; } = new();
}

public sealed class RemoteSettings
{
    public EventSettings Events { get; set; } = new();
    public LootSettings Loot { get; set; } = new();
    public ScreenshotSettings Screenshots { get; set; } = new();
    public FilterSettings Filters { get; set; } = new();
}

public sealed class EventSettings
{
    public bool Deaths { get; set; } = true;
    public bool Extracts { get; set; } = true;
    public bool Loot { get; set; } = true;
    public bool Quests { get; set; } = true;
    public bool BossKills { get; set; } = true;
    public bool LevelUps { get; set; } = true;
}
public sealed class LootSettings { public long MinimumValue { get; set; } = 500000; }
public sealed class ScreenshotSettings
{
    public bool Enabled { get; set; }
    public bool DeathScreenshots { get; set; } = true;
    public bool ExtractScreenshots { get; set; } = true;
    public bool RareLootScreenshots { get; set; } = true;
    public bool QuestScreenshots { get; set; } = true;
    public bool BossKillScreenshots { get; set; } = true;
}
public sealed class FilterSettings
{
    public int MinimumRaidDuration { get; set; }
    public List<string> IgnoredMaps { get; set; } = [];
}
