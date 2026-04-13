using System.Text.Json;
using Microsoft.AspNetCore.Routing;

namespace SysDash.NetCore.Backend.Endpoints;

public static class ApiEndpointMappings
{
    public static WebApplication MapSysDashApi(this WebApplication app)
    {
        MapReportEndpoints(app);
        MapHostEndpoints(app);
        MapServiceEndpoints(app);
        MapUnraidEndpoints(app);
        MapOmadaEndpoints(app);
        MapAssetEndpoints(app);
        MapIdracEndpoints(app);
        MapSummaryEndpoints(app);
        MapMetaEndpoints(app);

        return app;
    }

    private static void MapReportEndpoints(WebApplication app)
    {
        app.MapPost("/api/report", async (HttpContext context, IAppState state) =>
        {
            JsonDocument? doc;
            try
            {
                doc = await JsonDocument.ParseAsync(context.Request.Body);
            }
            catch
            {
                return Results.Json(new { error = "invalid json" }, statusCode: 400);
            }

            using (doc)
            {
                var root = doc.RootElement;
                var hostname = AppRequestHelpers.TryGetString(root, "hostname");
                var ip = AppRequestHelpers.TryGetString(root, "ip");
                var uptime = AppRequestHelpers.TryGetDouble(root, "uptime");
                var cpu = AppRequestHelpers.TryGetDouble(root, "cpu_percent") ?? AppRequestHelpers.TryGetDouble(root, "cpu");
                var memory = AppRequestHelpers.TryGetDouble(root, "memory_percent") ?? AppRequestHelpers.TryGetDouble(root, "memory");

                if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(ip) || uptime is null)
                {
                    return Results.Json(new { error = "missing fields" }, statusCode: 400);
                }

                state.UpsertHostReport(hostname, ip, uptime.Value, cpu, memory);
                return Results.Json(new { status = "ok" });
            }
        });
    }

    private static void MapHostEndpoints(WebApplication app)
    {
        app.MapGet("/api/status", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var rows = state.GetHostsForStatus();
            var results = new List<object>(rows.Count);

            foreach (var row in rows)
            {
                var ping = await AppRequestHelpers.TryPingMillisecondsAsync(row.ip, 1000);
                state.UpdatePing(row.hostname, ping);
                results.Add(new
                {
                    hostname = row.hostname,
                    ip = row.ip,
                    uptime = row.uptime,
                    last_seen = row.lastSeen,
                    ping_ms = ping,
                    cpu_percent = row.cpu,
                    memory_percent = row.memory,
                    friendly_name = row.friendlyName,
                    rdp_url = state.BuildRdpLaunchUrl(row.hostname, row.ip, row.rdpUrl),
                });
            }

            return Results.Json(results);
        });

        app.MapGet("/api/ping/{ip}", async (HttpContext context, string ip, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var pingMs = await AppRequestHelpers.TryPingMillisecondsAsync(ip, 1000);
            return Results.Json(new { ping_ms = pingMs });
        });

        app.MapGet("/api/system_status", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var status = new Dictionary<string, object?>
            {
                ["services"] = new List<object>(),
                ["docker"] = new List<object>(),
            };

            var services = (List<object>)status["services"]!;
            var docker = (List<object>)status["docker"]!;

            var mostRecent = state.GetMostRecentCheckin();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (mostRecent > 0 && (now - mostRecent) < 60)
            {
                services.Add(new { name = "SysDash Agent", status = "ok" });
            }
            else if (mostRecent > 0 && (now - mostRecent) < 180)
            {
                services.Add(new { name = "SysDash Agent", status = "warn" });
            }
            else
            {
                services.Add(new { name = "SysDash Agent", status = "down" });
            }

            foreach (var item in state.GetDockerStatus())
            {
                docker.Add(item);
            }

            return Results.Json(status);
        });
    }

    private static void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet("/api/services_legacy", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var results = await state.CollectServiceStatusesAsync();
            return Results.Json(new { services = results });
        });

        app.MapGet("/api/services", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var snapshot = state.GetServiceCacheSnapshot();
            if (snapshot.updatedAt == 0)
            {
                var warm = await state.CollectServiceStatusesAsync();
                state.SetServiceCache(warm);
                snapshot = state.GetServiceCacheSnapshot();
            }

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Json(new { services = snapshot.items, updated_at = snapshot.updatedAt });
        });

        app.MapGet("/api/services/debug", (IAppState state) =>
        {
            var snapshot = state.GetServiceCacheSnapshot();
            return Results.Json(new
            {
                cache = snapshot.items,
                state = new { last_updated = snapshot.updatedAt, monitor_running = state.ServiceMonitorStarted },
                monitor_running = state.ServiceMonitorStarted,
            });
        });
    }

    private static void MapUnraidEndpoints(WebApplication app)
    {
        app.MapGet("/api/unraid/debug", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var snapshot = await state.FetchUnraidSnapshotAsync();
            return Results.Json(new
            {
                url = $"https://{state.UnraidHost}/graphql",
                status_code = snapshot.statusCode,
                body = snapshot.rawBody,
                error = snapshot.error,
            });
        });

        app.MapGet("/api/unraid", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var snapshot = state.GetUnraidSnapshot();
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Json(snapshot);
        });

        app.MapGet("/api/unraid/refresh", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            try
            {
                var fetched = await state.FetchUnraidSnapshotAsync();
                state.SetUnraidSnapshot(fetched.normalized);
                return Results.Json(new { status = "ok", result = fetched.normalized });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", error = ex.Message }, statusCode: 500);
            }
        });
    }

    private static void MapAssetEndpoints(WebApplication app)
    {
        app.MapGet("/api/assets", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            return Results.Json(state.GetAssets());
        });

        app.MapPut("/api/assets/{hostname}", async (HttpContext context, string hostname, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            JsonDocument? doc;
            try
            {
                doc = await JsonDocument.ParseAsync(context.Request.Body);
            }
            catch
            {
                return Results.Json(new { error = "invalid json" }, statusCode: 400);
            }

            using (doc)
            {
                var root = doc.RootElement;
                var friendlyName = AppRequestHelpers.TryGetString(root, "friendly_name");
                var newIp = AppRequestHelpers.TryGetString(root, "ip");
                var rdpUrl = AppRequestHelpers.TryGetString(root, "rdp_url");
                state.UpdateAsset(hostname, friendlyName, newIp, rdpUrl);
                return Results.Json(new { status = "ok" });
            }
        });

        app.MapDelete("/api/assets/{hostname}", (HttpContext context, string hostname, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            state.DeleteAsset(hostname);
            return Results.Json(new { status = "ok" });
        });

        app.MapPost("/api/assets/{hostname}/move-up", (HttpContext context, string hostname, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var moved = state.MoveAsset(hostname, moveUp: true);
            if (!moved)
            {
                return Results.Json(new { error = "asset not found" }, statusCode: 404);
            }

            return Results.Json(new { status = "ok" });
        });

        app.MapPost("/api/assets/{hostname}/move-down", (HttpContext context, string hostname, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var moved = state.MoveAsset(hostname, moveUp: false);
            if (!moved)
            {
                return Results.Json(new { error = "asset not found" }, statusCode: 404);
            }

            return Results.Json(new { status = "ok" });
        });
    }

    private static void MapOmadaEndpoints(WebApplication app)
    {
        app.MapGet("/api/omada", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var snapshot = state.GetOmadaSnapshot();
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Json(snapshot);
        });

        app.MapGet("/api/omada/refresh", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            try
            {
                var fetched = await state.FetchOmadaSnapshotAsync();
                state.SetOmadaSnapshot(fetched);
                return Results.Json(new { status = "ok", result = fetched });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", error = ex.Message }, statusCode: 500);
            }
        });

        app.MapGet("/api/omada/detail", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            var requestedSiteId = context.Request.Query["site_id"].ToString();
            if (string.IsNullOrWhiteSpace(requestedSiteId))
            {
                requestedSiteId = null;
            }

            try
            {
                var detail = await state.FetchOmadaDetailAsync(requestedSiteId);
                return Results.Json(detail);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });
    }

    private static void MapSummaryEndpoints(WebApplication app)
    {
        app.MapGet("/api/summary", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            return Results.Json(state.GetSummaryPayload());
        });

        app.MapGet("/api/mobile/summary", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            return Results.Json(state.GetSummaryPayload());
        });
    }

    private static void MapIdracEndpoints(WebApplication app)
    {
        app.MapGet("/api/idrac", async (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            var summary = state.GetIdracSnapshot();

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Json(summary);
        });

        app.MapGet("/api/idrac/refresh", (HttpContext context, IAppState state) =>
        {
            if (!AppRequestHelpers.RequireKey(context, state.RequiredKey, out var denied))
            {
                return denied;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var fetched = await state.FetchIdracSummaryAsync();
                    state.SetIdracSnapshot(fetched);
                }
                catch
                {
                    // Keep the previous snapshot if refresh fails.
                }
            });

            return Results.Json(new
            {
                status = "queued",
                message = "iDRAC refresh started in background",
                snapshot = state.GetIdracSnapshot(),
            });
        });
    }

    private static void MapMetaEndpoints(WebApplication app)
    {
        app.MapGet("/api/version", (IAppState state) => Results.Json(new
        {
            api_build = state.ApiBuild,
            file = Path.Combine(app.Environment.ContentRootPath, "Program.cs"),
            pid = Environment.ProcessId,
            time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        }));

        app.MapGet("/api/routes", (EndpointDataSource endpointDataSource, IAppState state) =>
        {
            var routes = endpointDataSource.Endpoints
                .Select(e => e is RouteEndpoint re ? re.RoutePattern.RawText : e.DisplayName)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .OrderBy(e => e)
                .ToList();

            return Results.Json(new
            {
                api_build = state.ApiBuild,
                count = routes.Count,
                routes,
            });
        });
    }
}
