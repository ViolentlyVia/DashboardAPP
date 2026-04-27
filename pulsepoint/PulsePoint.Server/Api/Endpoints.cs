using PulsePoint.Models;

namespace PulsePoint.Api;

public static class Endpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Agent check-in — no auth required
        api.MapPost("/report", async (CheckInPayload payload, AppState state) =>
        {
            if (string.IsNullOrWhiteSpace(payload.Hostname))
                return Results.BadRequest("hostname required");
            state.Db.Upsert(payload);
            return Results.Ok(new { ok = true });
        });

        // ── API-key secured endpoints ─────────────────────────
        var secured = api.MapGroup("").AddEndpointFilter(ApiKeyFilter.Check);

        secured.MapGet("/hosts", (AppState state) =>
            Results.Ok(state.Db.GetAllHosts()));

        secured.MapGet("/hosts/{hostname}", (string hostname, AppState state) =>
        {
            var host = state.Db.GetHost(hostname);
            return host is null ? Results.NotFound() : Results.Ok(host);
        });

        secured.MapGet("/hosts/{hostname}/ping", async (string hostname, AppState state) =>
        {
            var host = state.Db.GetHost(hostname);
            if (host is null) return Results.NotFound();
            var ms = await state.PingAsync(host.Ip);
            state.Db.UpdatePing(hostname, ms);
            return Results.Ok(new { ip = host.Ip, ping_ms = ms, online = ms.HasValue });
        });

        secured.MapPut("/assets/{hostname}", (string hostname, AssetUpdatePayload payload, AppState state) =>
        {
            state.Db.UpdateAsset(hostname, payload);
            return Results.Ok(new { ok = true });
        });

        secured.MapDelete("/assets/{hostname}", (string hostname, AppState state) =>
        {
            state.Db.Delete(hostname);
            return Results.Ok(new { ok = true });
        });

        secured.MapPost("/assets/{hostname}/move-up", (string hostname, AppState state) =>
        {
            state.Db.MoveOrder(hostname, -1);
            return Results.Ok(new { ok = true });
        });

        secured.MapPost("/assets/{hostname}/move-down", (string hostname, AppState state) =>
        {
            state.Db.MoveOrder(hostname, 1);
            return Results.Ok(new { ok = true });
        });

        secured.MapGet("/services", async (AppState state) =>
            Results.Ok(await state.GetServicesAsync()));

        secured.MapGet("/services/refresh", async (AppState state) =>
            Results.Ok(await state.GetServicesAsync(forceRefresh: true)));

        secured.MapGet("/summary", async (AppState state) =>
        {
            var hosts = state.Db.GetAllHosts();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var online = hosts.Count(h => h.LastSeen >= now - 120);
            var services = await state.GetServicesAsync();

            return Results.Ok(new
            {
                hosts = new
                {
                    total = hosts.Count,
                    online,
                    offline = hosts.Count - online,
                    list = hosts
                },
                services = new
                {
                    total = services.Count,
                    online = services.Count(s => s.Online),
                    offline = services.Count(s => !s.Online),
                    list = services
                },
                generated_at = now
            });
        });

        secured.MapGet("/version", () => Results.Ok(new
        {
            version = "1.0.0",
            dotnet = Environment.Version.ToString(),
            pid = Environment.ProcessId,
            uptime_s = Environment.TickCount64 / 1000
        }));

        // ── Management endpoints (session-cookie auth) ────────
        var mgmt = api.MapGroup("/manage").AddEndpointFilter(ManageSessionFilter.Check);

        mgmt.MapGet("/services", (AppState state) =>
            Results.Ok(state.Db.GetServices()));

        mgmt.MapPost("/services", (AddServicePayload payload, AppState state) =>
        {
            if (string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.Address))
                return Results.BadRequest("name and address required");
            state.Db.AddService(payload.Name.Trim(), payload.Address.Trim());
            state.InvalidateServiceCache();
            return Results.Ok(new { ok = true });
        });

        mgmt.MapDelete("/services/{id:int}", (int id, AppState state) =>
        {
            state.Db.DeleteService(id);
            state.InvalidateServiceCache();
            return Results.Ok(new { ok = true });
        });

        mgmt.MapPut("/assets/{hostname}/name", (string hostname, NamePayload payload, AppState state) =>
        {
            if (string.IsNullOrWhiteSpace(payload.Name))
                return Results.BadRequest("name required");
            state.Db.UpdateFriendlyName(hostname, payload.Name.Trim());
            return Results.Ok(new { ok = true });
        });

        mgmt.MapGet("/assets", (AppState state) =>
            Results.Ok(state.Db.GetAllHosts()));
    }
}

public record NamePayload(string Name);

internal static class ApiKeyFilter
{
    public static async ValueTask<object?> Check(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var req = ctx.HttpContext.Request;
        var expected = ctx.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["ApiKey"];

        var provided = req.Query["key"].FirstOrDefault()
            ?? req.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(expected) || provided != expected)
            return Results.Json(new { error = "unauthorized" },
                statusCode: StatusCodes.Status401Unauthorized);

        return await next(ctx);
    }
}

internal static class ManageSessionFilter
{
    public static async ValueTask<object?> Check(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var state = ctx.HttpContext.RequestServices.GetRequiredService<AppState>();
        if (!state.ValidateSession(ctx.HttpContext))
            return Results.Json(new { error = "not authenticated" },
                statusCode: StatusCodes.Status401Unauthorized);
        return await next(ctx);
    }
}
