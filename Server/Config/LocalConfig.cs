using System.Text.Json.Serialization;

namespace SPTDiscordReports.Server.Config;

public sealed class LocalConfig
{
    public bool Enabled { get; set; } = true;
    public List<WebhookDestination> Webhooks { get; set; } = [];
    public int RefreshIntervalMinutes { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxRetries { get; set; } = 3;
}

public sealed class WebhookDestination
{
    public string Name { get; set; } = "Discord";
    public string Url { get; set; } = string.Empty;
    public string ConfigUrl { get; set; } = string.Empty;
}
