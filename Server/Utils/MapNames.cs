namespace DiscordRaidFeed.Server.Utils;

public static class MapNames
{
    private static readonly Dictionary<string, string> Mapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = "Customs",
        ["Woods"] = "Woods",
        ["Shoreline"] = "Shoreline",
        ["Interchange"] = "Interchange",
        ["RezervBase"] = "Reserve",
        ["laboratory"] = "The Lab",
        ["Lighthouse"] = "Lighthouse",
        ["TarkovStreets"] = "Streets of Tarkov",
        ["factory4_day"] = "Factory (Day)",
        ["factory4_night"] = "Factory (Night)",
        ["factory4"] = "Factory",
        ["Sandbox"] = "Ground Zero",
        ["Sandbox_high"] = "Ground Zero (High)",
        ["develop"] = "Develop",
    };

    public static string Resolve(string? mapId) => string.IsNullOrWhiteSpace(mapId) ? "Unknown" : (Mapping.TryGetValue(mapId, out var name) ? name : mapId);
}
