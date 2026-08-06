using System.Globalization;
using SPTarkov.DI.Annotations;
using SPTDiscordReports.Server.Discord;
using SPTDiscordReports.Server.Events;
using SPTDiscordReports.Server.Utils;

namespace SPTDiscordReports.Server.Services;

[Injectable(InjectionType.Singleton)]
public sealed class EventManager(DiscordWebhookService discord, ConfigService config, Log log)
{
    public void Publish(RaidEvent eventData)
    {
        try
        {
            if (eventData.Type == RaidEventType.Loot && eventData.Fields.TryGetValue("Value", out var value) && long.TryParse(value.Replace("₽", "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                if (!config.Local.Webhooks.Any(destination => config.Get(destination).Settings.Loot.MinimumValue <= amount)) return;
            }
            discord.Enqueue(eventData);
        }
        catch (Exception ex) { log.Error("Could not queue raid event.", ex); }
    }
}
