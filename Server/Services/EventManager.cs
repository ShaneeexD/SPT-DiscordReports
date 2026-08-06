using System.Globalization;
using SPTarkov.DI.Annotations;
using DiscordRaidFeed.Server.Discord;
using DiscordRaidFeed.Server.Events;
using DiscordRaidFeed.Server.Utils;

namespace DiscordRaidFeed.Server.Services;

[Injectable(InjectionType.Singleton)]
public sealed class EventManager(DiscordWebhookService discord, ConfigService config, Log log)
{
    public void Publish(RaidEvent eventData)
    {
        try
        {
            log.Info($"Publish called: type={eventData.Type}, player={eventData.Player}, map={eventData.Map}, raidTime={eventData.RaidTimeSeconds}s");
            if (eventData.Type == RaidEventType.Loot)
            {
                long amount = 0;
                if (eventData.Fields.TryGetValue("ValueRaw", out var raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    amount = parsed;
                else if (eventData.Fields.TryGetValue("Value", out var value) && long.TryParse(value.Replace("₽", "").Replace(",", "").Replace("k", "").Replace("M", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    amount = parsed;
                if (!config.Local.Webhooks.Any(destination => config.Get(destination).Settings.Loot.MinimumValue <= amount))
                {
                    log.Warning($"Loot event dropped: value {amount} below all configured minimums");
                    return;
                }
            }
            var enqueued = discord.Enqueue(eventData);
            log.Info($"Enqueue result: {enqueued} (queue had {config.Local.Webhooks.Count} webhooks)");
        }
        catch (Exception ex) { log.Error("Could not queue raid event.", ex); }
    }
}
