using SPTarkov.DI.Annotations;
using SPTDiscordReports.Server.Events;
using SPTDiscordReports.Server.Utils;

namespace SPTDiscordReports.Server.Services;

[Injectable(InjectionType.Singleton)]
public sealed class ScreenshotService(Log log)
{
    public async Task<byte[]?> ReadAsync(RaidEvent raidEvent, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(raidEvent.ScreenshotBase64))
        {
            try { return Convert.FromBase64String(raidEvent.ScreenshotBase64); }
            catch (FormatException) { log.Warning("Rejected invalid screenshot payload."); return null; }
        }
        if (string.IsNullOrWhiteSpace(raidEvent.ScreenshotPath)) return null;
        var path = Path.GetFullPath(raidEvent.ScreenshotPath);
        var allowedRoot = Path.GetFullPath(Path.Combine("user", "screenshots")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            log.Warning("Rejected screenshot path outside user/screenshots.");
            return null;
        }
        try { return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false); }
        catch (Exception ex) { log.Warning($"Could not read screenshot: {ex.Message}"); return null; }
    }
}
