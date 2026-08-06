using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SPTarkov.DI.Annotations;
using SPTDiscordReports.Server.Config;
using SPTDiscordReports.Server.Events;
using SPTDiscordReports.Server.Services;
using SPTDiscordReports.Server.Utils;

namespace SPTDiscordReports.Server.Discord;

[Injectable(InjectionType.Singleton)]
public sealed class DiscordWebhookService(ConfigService config, ScreenshotService screenshots, Log log) : IAsyncDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Channel<RaidEvent> _queue = Channel.CreateBounded<RaidEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    private CancellationTokenSource? _stop;
    private Task? _worker;

    public void Start() { _stop = new(); _worker = Task.Run(() => ConsumeAsync(_stop.Token)); }
    public bool Enqueue(RaidEvent eventData) => _queue.Writer.TryWrite(eventData);
    private async Task ConsumeAsync(CancellationToken token)
    {
        await foreach (var eventData in _queue.Reader.ReadAllAsync(token))
        {
            log.Info($"Processing event: type={eventData.Type}, player={eventData.Player}");
            foreach (var destination in config.Local.Webhooks.Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out _)))
            {
                if (!config.IsEnabled(destination, eventData.Type, eventData)) continue;
                log.Info($"Sending {eventData.Type} to webhook '{destination.Name}' at {destination.Url[..Math.Min(50, destination.Url.Length)]}...");
                try { var image = config.IsScreenshotEnabled(destination, eventData.Type) ? await screenshots.ReadAsync(eventData, token).ConfigureAwait(false) : null; await SendAsync(destination, eventData, image, token).ConfigureAwait(false); log.Info($"Webhook '{destination.Name}' sent successfully."); }
                catch (Exception ex) { log.Error($"Webhook '{destination.Name}' failed.", ex); }
            }
        }
    }
    private async Task SendAsync(WebhookDestination destination, RaidEvent eventData, byte[]? image, CancellationToken token)
    {
        var embed = EmbedBuilder.Build(eventData);
        if (image is not null) embed.Image = new DiscordEmbedImage { Url = "attachment://raid.png" };
        if (image is null)
        {
            using var content = new StringContent(JsonSerializer.Serialize(new DiscordWebhookPayload { Embeds = [embed] }), Encoding.UTF8, "application/json");
            await SendWithRetryAsync(destination.Url, content, token).ConfigureAwait(false);
            return;
        }
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(new DiscordWebhookPayload { Embeds = [embed] }), Encoding.UTF8, "application/json"), "payload_json");
        var file = new ByteArrayContent(image); file.Headers.ContentType = new MediaTypeHeaderValue("image/png"); multipart.Add(file, "files[0]", "raid.png");
        await SendWithRetryAsync(destination.Url, multipart, token).ConfigureAwait(false);
    }
    private async Task SendWithRetryAsync(string url, HttpContent content, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await _http.PostAsync(url, content, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            log.Warning($"Discord response: {(int)response.StatusCode} {response.StatusCode} - {body}");
            if (attempt >= Math.Max(0, config.Local.MaxRetries) || ((int)response.StatusCode < 500 && response.StatusCode != (HttpStatusCode)429)) throw new HttpRequestException($"Discord returned {(int)response.StatusCode}: {body}");
            var delay = response.StatusCode == (HttpStatusCode)429 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
    }
    public async ValueTask DisposeAsync() { if (_stop is null) return; _stop.Cancel(); _queue.Writer.TryComplete(); try { if (_worker is not null) await _worker; } catch (OperationCanceledException) { } _stop.Dispose(); _http.Dispose(); }
}
