# SPT Discord Raid Feed

SPT Discord Raid Feed is a local SPT server/client mod that posts raid activity to Discord through Discord Webhooks.

The server mod owns the webhook configuration and remote community settings. The optional client mod detects events that are only available inside the game client and sends them to the local SPT server.

## Supported events

The server side natively handles:

- Deaths
- Successful extractions

The client companion adds:

- Rare loot pickups
- Boss kills
- Quest completions
- Level-ups
- Client-side screenshots for supported events

All events are queued and sent asynchronously. Discord failures are logged and do not stop gameplay.

---

# Discord server owner setup

## 1. Create a Discord webhook

1. Open the Discord server and choose the target text channel.
2. Open **Edit Channel**.
3. Choose **Integrations**.
4. Choose **Webhooks**.
5. Create a webhook and select the destination channel.
6. Copy the webhook URL.

Treat the webhook URL like a password. Anyone who has it can post to that channel. Do not publish it in a public GitHub repository or commit it to source control, only share it to server users who want to announce raid feeds from their end.

If a webhook URL is exposed outside of your server or is being used maliciously, you can revoke it in the Discord webhook settings and get a new one.

## 2. Create the community configuration file

Create a JSON file in a GitHub repository. For example:

```json
{
  "configVersion": 1,
  "minimumModVersion": "1.0.0",
  "communityName": "Serenity Community",
  "settings": {
    "events": {
      "deaths": true,
      "extracts": true,
      "loot": true,
      "quests": true,
      "bossKills": true,
      "levelUps": true
    },
    "loot": {
      "minimumValue": 500000
    },
    "screenshots": {
      "enabled": true,
      "deathScreenshots": true,
      "extractScreenshots": true,
      "rareLootScreenshots": true,
      "questScreenshots": true,
      "bossKillScreenshots": true
    },
    "filters": {
      "minimumRaidDuration": 60,
      "ignoredMaps": []
    }
  }
}
```

### Remote configuration fields

| Field | Description |
|---|---|
| `configVersion` | Configuration schema version. The current supported version is `1`. Future unsupported versions are rejected. |
| `minimumModVersion` | Minimum Discord Raid Feed version required by the community configuration. |
| `communityName` | Display name for the community configuration. |
| `settings.events` | Enables or disables each event type. |
| `settings.loot.minimumValue` | Minimum calculated loot value required for a loot alert. |
| `settings.screenshots` | Enables screenshots globally and per event. |
| `settings.filters.minimumRaidDuration` | Ignores events from raids shorter than this number of seconds. `0` disables the filter. |
| `settings.filters.ignoredMaps` | Map names that should not produce notifications. |

## 3. Publish the configuration through GitHub

The mod downloads the raw JSON file, not the normal GitHub webpage URL.

Use a URL in this format:

```text
https://raw.githubusercontent.com/OWNER/REPOSITORY/BRANCH/discord-feed-config.json
```

For example:

```text
https://raw.githubusercontent.com/serenity-community/server-config/main/discord-feed-config.json
```

The repository and file can be public or otherwise reachable by the SPT installation. The SPT server must be able to access the URL.

## 4. Configure the server mod

Edit the deployed server configuration:

```text
SPT/user/mods/DiscordReports/config/config.json
```

Example with one community:

```json
{
  "enabled": true,
  "webhooks": [
    {
      "name": "Serenity Community",
      "url": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN",
      "configUrl": "https://raw.githubusercontent.com/serenity-community/server-config/main/discord-feed-config.json"
    }
  ],
  "refreshIntervalMinutes": 30,
  "requestTimeoutSeconds": 15,
  "maxRetries": 3
}
```

Restart the SPT server after changing the local configuration.

## 5. Configure multiple Discord communities

Add more entries to the `webhooks` array. Each destination can use a different remote configuration:

```json
{
  "enabled": true,
  "webhooks": [
    {
      "name": "Friends Server",
      "url": "https://discord.com/api/webhooks/FRIENDS_ID/FRIENDS_TOKEN",
      "configUrl": "https://raw.githubusercontent.com/example/friends-config/main/feed.json"
    },
    {
      "name": "Hardcore Server",
      "url": "https://discord.com/api/webhooks/HARDCORE_ID/HARDCORE_TOKEN",
      "configUrl": "https://raw.githubusercontent.com/example/hardcore-config/main/feed.json"
    }
  ],
  "refreshIntervalMinutes": 30,
  "requestTimeoutSeconds": 15,
  "maxRetries": 3
}
```

Each community can independently control event types, loot thresholds, screenshots, raid duration, and ignored maps.

## Remote configuration behavior

- The first successful download is cached in the server mod's `config/cache` directory.
- The configuration is refreshed using `refreshIntervalMinutes`.
- A SHA-256 hash is used to detect unchanged remote files.
- If GitHub is unavailable, the last valid cached configuration is used.
- Unsupported future `configVersion` values are rejected.
- A warning is logged if `minimumModVersion` is higher than the installed mod version.
- Invalid or unavailable remote settings do not stop the SPT server.

---

# Player/user setup

1. Install the mod and run SPT once. This creates the local configuration file.
2. Open:

   ```text
   SPT/user/mods/DiscordReports/config/config.json
   ```

3. Paste the webhook configuration supplied by the Discord server owner into the `webhooks` array. For example:

   ```json
   {
     "enabled": true,
     "webhooks": [
       {
         "name": "My Discord Server",
         "url": "DISCORD_WEBHOOK_URL_FROM_SERVER_OWNER",
         "configUrl": "REMOTE_CONFIG_URL_FROM_SERVER_OWNER"
       }
     ],
     "refreshIntervalMinutes": 30,
     "requestTimeoutSeconds": 15,
     "maxRetries": 3
   }
   ```

4. Save the file and restart SPT.

The Discord server owner should provide both the webhook `url` and the community `configUrl`. Do not share or publish the webhook URL.

If the client companion is included with the installation, run the game once after installing it so its BepInEx configuration is generated. The default local SPT server address normally requires no changes.

---

# Screenshots

Screenshots are controlled by both sides:

1. The client setting must have screenshots enabled.
2. The community's remote configuration must have screenshots enabled for that event type.

The client captures screenshots locally and sends them to the SPT server as part of the event payload. The server then attaches them to the Discord webhook message.

If the remote community configuration disables screenshots, the screenshot is not sent to Discord.

---

# Troubleshooting

## No Discord messages appear

Check the SPT server log for `[DiscordRaidFeed]` entries and verify:

- The webhook URL is correct.
- The webhook has not been deleted or regenerated.
- The server has internet access to Discord.
- The remote configuration URL returns JSON rather than an HTML GitHub page.
- `configVersion` is `1`.
- The event is enabled in the remote configuration.
- The map is not listed in `ignoredMaps`.
- The raid lasted longer than `minimumRaidDuration`.

## Loot notifications do not appear

Loot notifications are filtered by `settings.loot.minimumValue`. The client calculates the item's base handbook value multiplied by its stack count. Lower the threshold temporarily while testing.

## Client events do not appear

Verify:

- `SPTDiscordReports.Client.dll` is in `BepInEx/plugins/SPTDiscordReports`.
- BepInEx loaded the plugin without a Harmony error.
- `ServerUrl` points to the running SPT server.
- The server mod is installed and loaded.
- The game client can reach the server host and port.

## Screenshots do not appear

Verify that both the client setting and the remote community settings enable screenshots. Screenshots can also fail if the game cannot write to its temporary cache directory.

## Discord is unavailable

Gameplay continues normally. The server logs the failure and retries transient Discord failures according to `maxRetries`.

---

# Security and privacy

- Keep Discord webhook URLs private.
- Do not put webhook URLs in a public GitHub configuration file.
- Screenshots are sent to the configured Discord communities when enabled.
- The mod does not create user accounts or send data to a hosted service.
- Community owners should only share webhook access with trusted server administrators.


