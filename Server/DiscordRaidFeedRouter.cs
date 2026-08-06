using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using SPTDiscordReports.Server.Events;
using SPTDiscordReports.Server.Services;

namespace SPTDiscordReports.Server;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class DiscordRaidFeedRouter(JsonUtil jsonUtil, EventManager events) : StaticRouter(jsonUtil, [
    new RouteAction<RaidEventRequest>("/client/discordraidfeed/event", async (_, request, _, _, _) => { events.Publish(request.ToEvent()); return "null"; })
]) { }
