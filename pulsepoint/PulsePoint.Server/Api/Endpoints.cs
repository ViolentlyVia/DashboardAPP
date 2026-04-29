using PulsePoint.Models;

namespace PulsePoint.Api;

public static class Endpoints
{
    private static readonly HttpClient _proxyClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    }) { Timeout = TimeSpan.FromSeconds(10) };

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

        // ── Integration credential management ─────────────────
        mgmt.MapGet("/integrations", (AppState state) =>
        {
            var db = state.Db;
            return Results.Ok(new
            {
                unraid = new
                {
                    host        = db.GetSetting("unraid_host")         ?? "",
                    apiKey      = db.GetSetting("unraid_api_key")      ?? "",
                    apiKeyId    = db.GetSetting("unraid_api_key_id")   ?? "",
                    bearerToken = db.GetSetting("unraid_bearer_token") ?? ""
                },
                idrac = new
                {
                    host     = db.GetSetting("idrac_host")     ?? "",
                    username = db.GetSetting("idrac_username") ?? "",
                    // never return password back to UI
                    hasPassword = !string.IsNullOrEmpty(db.GetSetting("idrac_password"))
                }
            });
        });

        mgmt.MapPut("/integrations/unraid", (UnraidCredPayload payload, AppState state) =>
        {
            state.Db.SetSetting("unraid_host",         payload.Host.Trim());
            state.Db.SetSetting("unraid_api_key",      payload.ApiKey.Trim());
            state.Db.SetSetting("unraid_api_key_id",   payload.ApiKeyId?.Trim() ?? "");
            state.Db.SetSetting("unraid_bearer_token", payload.BearerToken?.Trim() ?? "");
            return Results.Ok(new { ok = true });
        });

        mgmt.MapPut("/integrations/idrac", (IdracCredPayload payload, AppState state) =>
        {
            state.Db.SetSetting("idrac_host",     payload.Host.Trim());
            state.Db.SetSetting("idrac_username", payload.Username.Trim());
            if (!string.IsNullOrEmpty(payload.Password))
                state.Db.SetSetting("idrac_password", payload.Password);
            return Results.Ok(new { ok = true });
        });

        // ── Integration data endpoints (API-key auth) ─────────
        secured.MapGet("/unraid", async (AppState state) =>
        {
            var snap = state.GetUnraidSnapshot();
            if (snap is null) snap = await state.RefreshUnraidAsync();
            return Results.Ok(snap);
        });

        secured.MapGet("/unraid/refresh", async (AppState state) =>
            Results.Ok(await state.RefreshUnraidAsync()));

        secured.MapPost("/unraid/docker/{id}/start",   async (string id, AppState state) =>
            Results.Ok(new { ok = await state.UnraidDockerActionAsync(id, "start") }));
        secured.MapPost("/unraid/docker/{id}/stop",    async (string id, AppState state) =>
            Results.Ok(new { ok = await state.UnraidDockerActionAsync(id, "stop") }));
        secured.MapPost("/unraid/docker/{id}/restart", async (string id, AppState state) =>
            Results.Ok(new { ok = await state.UnraidDockerActionAsync(id, "restart") }));

        secured.MapPost("/unraid/vm/{name}/start",   async (string name, AppState state) =>
            Results.Ok(new { ok = await state.UnraidVmActionAsync(name, "start") }));
        secured.MapPost("/unraid/vm/{name}/stop",    async (string name, AppState state) =>
            Results.Ok(new { ok = await state.UnraidVmActionAsync(name, "stop") }));
        secured.MapPost("/unraid/vm/{name}/restart", async (string name, AppState state) =>
            Results.Ok(new { ok = await state.UnraidVmActionAsync(name, "restart") }));

        secured.MapGet("/idrac", async (AppState state) =>
        {
            var snap = state.GetIdracSnapshot();
            if (snap is null) snap = await state.RefreshIdracAsync();
            return Results.Ok(snap);
        });

        secured.MapGet("/idrac/refresh", async (AppState state) =>
            Results.Ok(await state.RefreshIdracAsync()));

        secured.MapGet("/omada", async (AppState state) =>
        {
            var snap = state.GetOmadaSnapshot();
            if (snap is null) snap = await state.RefreshOmadaAsync();
            return Results.Ok(snap);
        });

        secured.MapGet("/omada/refresh", async (AppState state) =>
            Results.Ok(await state.RefreshOmadaAsync()));

        secured.MapGet("/omada/site/{siteId}", async (string siteId, AppState state) =>
            Results.Ok(await state.RefreshOmadaSiteAsync(siteId)));

        secured.MapPut("/omada/preferred-site/{siteId}", (string siteId, AppState state) =>
        {
            state.Db.SetSetting("omada_site_id", siteId);
            return Results.Ok(new { ok = true });
        });

        // ── Grow page ─────────────────────────────────────────
        secured.MapGet("/grow/info", async (AppState state) =>
        {
            var growUrl = state.Db.GetSetting("grow_url")?.TrimEnd('/');
            if (string.IsNullOrEmpty(growUrl))
                return Results.Content(
                    "<body style='background:#0c0c10;color:#f87171;font-family:sans-serif;padding:1rem'>" +
                    "Grow device URL is not configured. Set it on the Management page.</body>",
                    "text/html");
            try
            {
                var html = await _proxyClient.GetStringAsync($"{growUrl}/");
                // fix relative resource paths so the iframe can load CSS/images
                if (!html.Contains("<base ", StringComparison.OrdinalIgnoreCase))
                    html = html.Replace("<head>", $"<head><base href=\"{growUrl}/\">",
                                        StringComparison.OrdinalIgnoreCase);
                return Results.Content(html, "text/html");
            }
            catch (Exception ex)
            {
                return Results.Content(
                    $"<body style='background:#0c0c10;color:#f87171;font-family:sans-serif;padding:1rem'>" +
                    $"Could not reach Grow device — {System.Net.WebUtility.HtmlEncode(ex.Message)}</body>",
                    "text/html");
            }
        });

        // ── Omada credential management ───────────────────────
        mgmt.MapGet("/integrations/omada", (AppState state) =>
        {
            var db = state.Db;
            return Results.Ok(new
            {
                baseUrl      = db.GetSetting("omada_base_url")      ?? "",
                omadacId     = db.GetSetting("omada_omadac_id")     ?? "",
                clientId     = db.GetSetting("omada_client_id")     ?? "",
                hasSecret    = !string.IsNullOrEmpty(db.GetSetting("omada_client_secret")),
                preferSiteId = db.GetSetting("omada_site_id")       ?? ""
            });
        });

        mgmt.MapPut("/integrations/omada", (OmadaCredPayload payload, AppState state) =>
        {
            state.Db.SetSetting("omada_base_url",      payload.BaseUrl.Trim());
            state.Db.SetSetting("omada_omadac_id",     payload.OmadacId.Trim());
            state.Db.SetSetting("omada_client_id",     payload.ClientId.Trim());
            if (!string.IsNullOrEmpty(payload.ClientSecret))
                state.Db.SetSetting("omada_client_secret", payload.ClientSecret);
            state.Db.SetSetting("omada_site_id",       payload.PreferSiteId?.Trim() ?? "");
            return Results.Ok(new { ok = true });
        });

        // ── Grow credential management ────────────────────────
        mgmt.MapGet("/integrations/grow", (AppState state) =>
        {
            var url = state.Db.GetSetting("grow_url") ?? "";
            return Results.Ok(new { url, configured = !string.IsNullOrEmpty(url) });
        });

        mgmt.MapPut("/integrations/grow", (GrowCredPayload payload, AppState state) =>
        {
            state.Db.SetSetting("grow_url", payload.Url.Trim());
            return Results.Ok(new { ok = true });
        });
    }
}

public record NamePayload(string Name);
public record UnraidCredPayload(string Host, string ApiKey, string? ApiKeyId, string? BearerToken);
public record IdracCredPayload(string Host, string Username, string? Password);
public record OmadaCredPayload(string BaseUrl, string OmadacId, string ClientId, string? ClientSecret, string? PreferSiteId);
public record GrowCredPayload(string Url);

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
