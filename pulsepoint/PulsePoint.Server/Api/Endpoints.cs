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
        secured.MapGet("/grow/status", async (AppState state) =>
        {
            var growUrl = state.Db.GetSetting("grow_url")?.TrimEnd('/');
            if (string.IsNullOrEmpty(growUrl))
                return Results.Json(new { configured = false });
            try
            {
                var json = await _proxyClient.GetStringAsync($"{growUrl}/api/status");
                using var _ = System.Text.Json.JsonDocument.Parse(json);
                return Results.Content(
                    $"{{\"configured\":true,\"connected\":true,\"data\":{json}}}",
                    "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { configured = true, connected = false, error = ex.Message });
            }
        });

        secured.MapPost("/grow/pump", async (HttpContext http, AppState state) =>
        {
            var growUrl = state.Db.GetSetting("grow_url")?.TrimEnd('/');
            if (string.IsNullOrEmpty(growUrl))
                return Results.Json(new { ok = false, error = "not configured" });
            var form   = await http.Request.ReadFormAsync();
            var action = form["action"].FirstOrDefault() ?? "";
            try
            {
                var content = new FormUrlEncodedContent(
                    new[] { new KeyValuePair<string, string>("action", action) });
                var resp = await _proxyClient.PostAsync($"{growUrl}/api/pump", content);
                return Results.Json(new { ok = resp.IsSuccessStatusCode });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        secured.MapPost("/grow/set", async (HttpContext http, AppState state) =>
        {
            var growUrl = state.Db.GetSetting("grow_url")?.TrimEnd('/');
            if (string.IsNullOrEmpty(growUrl))
                return Results.Json(new { ok = false, error = "not configured" });
            var form  = await http.Request.ReadFormAsync();
            var pairs = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrEmpty(form["threshold"]))
                pairs.Add(new KeyValuePair<string, string>("threshold", form["threshold"].ToString()));
            if (!string.IsNullOrEmpty(form["pump_dur"]))
                pairs.Add(new KeyValuePair<string, string>("pump_dur", form["pump_dur"].ToString()));
            if (pairs.Count == 0)
                return Results.Json(new { ok = false, error = "no parameters provided" });
            try
            {
                var content = new FormUrlEncodedContent(pairs);
                var resp    = await _proxyClient.PostAsync($"{growUrl}/api/set", content);
                return Results.Json(new { ok = resp.IsSuccessStatusCode });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        secured.MapPost("/grow/history/clear", async (AppState state) =>
        {
            var growUrl = state.Db.GetSetting("grow_url")?.TrimEnd('/');
            if (string.IsNullOrEmpty(growUrl))
                return Results.Json(new { ok = false, error = "not configured" });
            try
            {
                var resp = await _proxyClient.PostAsync($"{growUrl}/api/history/clear",
                    new StringContent(""));
                return Results.Json(new { ok = resp.IsSuccessStatusCode });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
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
            var url     = state.Db.GetSetting("grow_url")      ?? "";
            var rtspUrl = state.Db.GetSetting("grow_rtsp_url") ?? "";
            var hlsUrl  = state.Db.GetSetting("grow_hls_url")  ?? "";
            return Results.Ok(new { url, rtspUrl, hlsUrl, configured = !string.IsNullOrEmpty(url) });
        });

        mgmt.MapPut("/integrations/grow", (GrowCredPayload payload, AppState state) =>
        {
            state.Db.SetSetting("grow_url",      payload.Url.Trim());
            state.Db.SetSetting("grow_rtsp_url", payload.RtspUrl?.Trim() ?? "");
            state.Db.SetSetting("grow_hls_url",  payload.HlsUrl?.Trim()  ?? "");
            return Results.Ok(new { ok = true });
        });

        // ── Appearance settings ───────────────────────────────
        mgmt.MapGet("/appearance", (AppState state) =>
        {
            var ap = state.GetAppearance();
            return Results.Ok(new
            {
                accentColor        = ap.AccentColor,
                siteName           = ap.SiteName,
                navHidden          = ap.NavHidden,
                cardColumns        = ap.CardColumns,
                hiddenMetrics      = ap.HiddenMetrics,
                refreshInterval    = ap.RefreshInterval,
                onlineThreshold    = ap.OnlineThreshold,
                hideServicesWidget = ap.HideServicesWidget
            });
        });

        mgmt.MapPut("/appearance", (AppearancePayload payload, AppState state) =>
        {
            state.Db.SetSetting("ui_accent_color",          payload.AccentColor.Trim());
            state.Db.SetSetting("ui_site_name",             payload.SiteName.Trim());
            state.Db.SetSetting("ui_nav_hidden",            payload.NavHidden.Trim());
            state.Db.SetSetting("ui_card_columns",          payload.CardColumns.Trim());
            state.Db.SetSetting("ui_hidden_metrics",        payload.HiddenMetrics.Trim());
            state.Db.SetSetting("ui_refresh_interval",      payload.RefreshInterval.ToString());
            state.Db.SetSetting("ui_online_threshold",      payload.OnlineThreshold.ToString());
            state.Db.SetSetting("ui_hide_services_widget",  payload.HideServicesWidget ? "true" : "");
            return Results.Ok(new { ok = true });
        });
    }
}

public record NamePayload(string Name);
public record UnraidCredPayload(string Host, string ApiKey, string? ApiKeyId, string? BearerToken);
public record IdracCredPayload(string Host, string Username, string? Password);
public record OmadaCredPayload(string BaseUrl, string OmadacId, string ClientId, string? ClientSecret, string? PreferSiteId);
public record GrowCredPayload(string Url, string? RtspUrl, string? HlsUrl);
public record AppearancePayload(string AccentColor, string SiteName, string NavHidden, string CardColumns, string HiddenMetrics, int RefreshInterval, int OnlineThreshold, bool HideServicesWidget);

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
