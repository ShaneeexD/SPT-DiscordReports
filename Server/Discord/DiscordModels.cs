using System.Text.Json.Serialization;

namespace DiscordRaidFeed.Server.Discord;

public sealed class DiscordWebhookPayload
{
    [JsonPropertyName("username")] public string Username { get; set; } = "Discord Raid Feed";
    [JsonPropertyName("embeds")] public List<DiscordEmbed> Embeds { get; set; } = [];
}
public sealed class DiscordEmbed
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("color")] public int Color { get; set; }
    [JsonPropertyName("fields")] public List<DiscordEmbedField> Fields { get; set; } = [];
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("footer")] public DiscordEmbedFooter Footer { get; set; } = new();
    [JsonPropertyName("image")] public DiscordEmbedImage? Image { get; set; }
}
public sealed class DiscordEmbedField { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("value")] public string Value { get; set; } = ""; [JsonPropertyName("inline")] public bool Inline { get; set; } = true; }
public sealed class DiscordEmbedFooter { [JsonPropertyName("text")] public string Text { get; set; } = "Discord Raid Feed"; }
public sealed class DiscordEmbedImage { [JsonPropertyName("url")] public string Url { get; set; } = ""; }
