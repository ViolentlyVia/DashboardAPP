using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SysDash.NetCore.Backend;

public sealed partial class AppState
{
    public Dictionary<string, object?> GetUnraidSnapshot()
    {
        lock (_unraidLock)
        {
            return new Dictionary<string, object?>(_unraidSnapshot);
        }
    }

    public void SetUnraidSnapshot(Dictionary<string, object?> snapshot)
    {
        lock (_unraidLock)
        {
            _unraidSnapshot = snapshot;
        }
    }

    public async Task<(Dictionary<string, object?> normalized, int? statusCode, object? rawBody, string? error)> FetchUnraidSnapshotAsync()
    {
        var source = new Dictionary<string, object?>
        {
            ["base_url"] = $"https://{UnraidHost}",
            ["successful"] = new List<object>(),
            ["failed"] = new List<object>(),
        };
        var successful = (List<object>)source["successful"]!;
        var failed = (List<object>)source["failed"]!;

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{UnraidHost}/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = UnraidGraphQuery }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-API-Key", UnraidApiKey);
        request.Headers.Add("X-API-Key-ID", UnraidApiKeyId);
        if (!string.IsNullOrWhiteSpace(UnraidBearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UnraidBearerToken);
        }
        if (!string.IsNullOrWhiteSpace(UnraidSessionCookie))
        {
            request.Headers.Add("Cookie", UnraidSessionCookie);
        }

        try
        {
            using var response = await _unraidClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (response.IsSuccessStatusCode && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    successful.Add(new Dictionary<string, object?>
                    {
                        ["endpoint"] = "/graphql",
                        ["status_code"] = (int)response.StatusCode,
                        ["content_type"] = contentType,
                    });
                    var normalized = NormalizeUnraidData(data, new List<object> { source }, null);
                    return (normalized, (int)response.StatusCode, JsonSerializer.Deserialize<object>(body), null);
                }

                failed.Add(new Dictionary<string, object?>
                {
                    ["endpoint"] = "/graphql",
                    ["status_code"] = (int)response.StatusCode,
                    ["reason"] = "No data returned",
                });
            }
            else
            {
                failed.Add(new Dictionary<string, object?>
                {
                    ["endpoint"] = "/graphql",
                    ["status_code"] = (int)response.StatusCode,
                    ["reason"] = body.Length > 300 ? body[..300] : body,
                });
            }

            var error = FirstUnraidFailureReason(new List<object> { source }) ?? "Unraid data unavailable.";
            var fallback = NormalizeUnraidData(default, new List<object> { source }, error);
            return (fallback, (int)response.StatusCode, body, error);
        }
        catch (Exception ex)
        {
            failed.Add(new Dictionary<string, object?>
            {
                ["endpoint"] = "/graphql",
                ["reason"] = ex.Message,
            });
            var error = FirstUnraidFailureReason(new List<object> { source }) ?? ex.Message;
            var fallback = NormalizeUnraidData(default, new List<object> { source }, error);
            return (fallback, null, null, ex.Message);
        }
    }

    private static Dictionary<string, object?> NormalizeUnraidData(JsonElement data, List<object> sources, string? explicitError)
    {
        Dictionary<string, object?>? parsedArray = null;
        Dictionary<string, object?>? parsedDocker = null;
        List<Dictionary<string, object?>>? parsedShares = null;
        List<double>? temps = null;

        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("array", out var arrayNode) && arrayNode.ValueKind == JsonValueKind.Object)
            {
                var disks = new List<Dictionary<string, object?>>();
                ParseDiskList(arrayNode, "disks", disks);
                ParseDiskList(arrayNode, "parities", disks);
                parsedArray = new Dictionary<string, object?>
                {
                    ["state"] = AppRequestHelpers.TryGetString(arrayNode, "state")?.ToUpperInvariant() ?? "UNKNOWN",
                    ["disks"] = disks,
                };
            }

            if (data.TryGetProperty("docker", out var dockerNode) && dockerNode.ValueKind == JsonValueKind.Object)
            {
                var containers = new List<Dictionary<string, object?>>();
                if (dockerNode.TryGetProperty("containers", out var ctNode) && ctNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in ctNode.EnumerateArray())
                    {
                        var names = new List<string>();
                        if (item.TryGetProperty("names", out var namesNode) && namesNode.ValueKind == JsonValueKind.Array)
                        {
                            names.AddRange(namesNode.EnumerateArray().Select(n => n.GetString()).Where(s => !string.IsNullOrWhiteSpace(s))!);
                        }

                        var state = AppRequestHelpers.TryGetString(item, "state") ?? AppRequestHelpers.TryGetString(item, "status") ?? "unknown";
                        var normalized = NormalizeContainerState(state);
                        containers.Add(new Dictionary<string, object?>
                        {
                            ["names"] = names,
                            ["state"] = normalized,
                            ["status"] = AppRequestHelpers.TryGetString(item, "status") ?? state,
                        });
                    }
                }

                parsedDocker = new Dictionary<string, object?>
                {
                    ["running"] = containers.Count(c => string.Equals(c["state"] as string, "RUNNING", StringComparison.Ordinal)),
                    ["stopped"] = containers.Count(c => !string.Equals(c["state"] as string, "RUNNING", StringComparison.Ordinal)),
                    ["containers"] = containers,
                };
            }

            if (data.TryGetProperty("shares", out var sharesNode) && sharesNode.ValueKind == JsonValueKind.Array)
            {
                parsedShares = new List<Dictionary<string, object?>>();
                foreach (var share in sharesNode.EnumerateArray())
                {
                    var name = AppRequestHelpers.TryGetString(share, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    parsedShares.Add(new Dictionary<string, object?>
                    {
                        ["name"] = name,
                        ["free"] = AppRequestHelpers.TryGetDouble(share, "free") ?? 0,
                        ["size"] = AppRequestHelpers.TryGetDouble(share, "size") ?? 0,
                    });
                }
            }

            if (data.TryGetProperty("info", out var infoNode)
                && infoNode.TryGetProperty("cpu", out var cpuNode)
                && cpuNode.TryGetProperty("packages", out var packagesNode)
                && packagesNode.TryGetProperty("temp", out var tempNode)
                && tempNode.ValueKind == JsonValueKind.Array)
            {
                temps = tempNode.EnumerateArray()
                    .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
            }
        }

        var result = new Dictionary<string, object?>
        {
            ["fetched_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["gql_available"] = parsedArray is not null || parsedDocker is not null || parsedShares is not null,
            ["array"] = parsedArray ?? new Dictionary<string, object?> { ["error"] = explicitError ?? "Array data unavailable" },
            ["docker"] = parsedDocker ?? new Dictionary<string, object?> { ["error"] = explicitError ?? "Docker data unavailable" },
            ["shares"] = parsedShares is null ? new List<object>() : parsedShares,
            ["sources"] = sources,
            ["error"] = explicitError,
        };

        if (temps is { Count: > 0 })
        {
            result["cpu_temps"] = temps;
            result["cpu_temp_avg"] = Math.Round(temps.Average(), 1);
        }

        if (result["error"] is null && parsedArray is null && parsedDocker is null)
        {
            result["error"] = explicitError ?? "Unraid data unavailable. Check API key and HTTPS connectivity.";
        }

        return result;
    }

    private static void ParseDiskList(JsonElement arrayNode, string fieldName, List<Dictionary<string, object?>> output)
    {
        if (!arrayNode.TryGetProperty(fieldName, out var listNode) || listNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var disk in listNode.EnumerateArray())
        {
            var name = AppRequestHelpers.TryGetString(disk, "name")
                ?? AppRequestHelpers.TryGetString(disk, "device")
                ?? AppRequestHelpers.TryGetString(disk, "id")
                ?? "disk";
            var status = (AppRequestHelpers.TryGetString(disk, "status") ?? AppRequestHelpers.TryGetString(disk, "state") ?? "UNKNOWN").ToUpperInvariant();
            var entry = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["status"] = status,
            };
            var temp = AppRequestHelpers.TryGetDouble(disk, "temp");
            if (temp.HasValue)
            {
                entry["temp"] = temp.Value;
            }
            output.Add(entry);
        }
    }

    private static string NormalizeContainerState(string state)
    {
        var stateText = state.ToUpperInvariant();
        if (stateText.Contains("RUN", StringComparison.Ordinal))
        {
            return "RUNNING";
        }

        if (stateText.Contains("STOP", StringComparison.Ordinal) || stateText.Contains("EXIT", StringComparison.Ordinal))
        {
            return "STOPPED";
        }

        return stateText;
    }

    private static string? FirstUnraidFailureReason(List<object> sources)
    {
        foreach (var source in sources.OfType<Dictionary<string, object?>>())
        {
            if (source.TryGetValue("failed", out var failedObj) && failedObj is List<object> failed)
            {
                foreach (var entry in failed.OfType<Dictionary<string, object?>>())
                {
                    var reason = entry.TryGetValue("reason", out var rv) ? rv?.ToString() : null;
                    var endpoint = entry.TryGetValue("endpoint", out var ev) ? ev?.ToString() : "unknown endpoint";
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        return $"{endpoint}: {reason}";
                    }
                }
            }
        }

        return null;
    }
}
