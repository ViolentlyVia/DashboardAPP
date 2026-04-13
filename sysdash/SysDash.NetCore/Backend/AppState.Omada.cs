using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SysDash.NetCore.Backend;

public sealed partial class AppState
{
    public Dictionary<string, object?> GetOmadaSnapshot()
    {
        lock (_omadaLock)
        {
            return new Dictionary<string, object?>(_omadaSnapshot);
        }
    }

    public void SetOmadaSnapshot(Dictionary<string, object?> snapshot)
    {
        lock (_omadaLock)
        {
            _omadaSnapshot = snapshot;
        }
    }

    public async Task<Dictionary<string, object?>> FetchOmadaSnapshotAsync()
    {
        var fetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var snapshot = new Dictionary<string, object?>
        {
            ["fetched_at"] = fetchedAt,
            ["connected"] = false,
            ["error"] = null,
            ["controller"] = new Dictionary<string, object?>
            {
                ["base_url"] = OmadaBaseUrl,
                ["omadac_id"] = OmadaOmadacId,
                ["configured_site_id"] = string.IsNullOrWhiteSpace(OmadaSiteId) ? null : OmadaSiteId,
            },
            ["sites"] = new List<object>(),
            ["selected_site"] = null,
            ["device_count_total"] = null,
            ["client_count_total"] = null,
            ["sources"] = new List<object>(),
        };

        var sources = (List<object>)snapshot["sources"]!;

        lock (_omadaLock)
        {
            _omadaLastAuthError = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(OmadaOmadacId)
            || string.IsNullOrWhiteSpace(OmadaClientId)
            || string.IsNullOrWhiteSpace(OmadaClientSecret))
        {
            snapshot["error"] = "Omada is not configured. Set OMADA_OMADAC_ID, OMADA_CLIENT_ID, and OMADA_CLIENT_SECRET.";
            return snapshot;
        }

        var sitesCall = await SendOmadaRequestAsync(HttpMethod.Get, $"/openapi/v1/{OmadaOmadacId}/sites?page=1&pageSize=100", retryAuth: true);
        if (!sitesCall.success || sitesCall.body is null)
        {
            var authError = string.Empty;
            lock (_omadaLock)
            {
                authError = _omadaLastAuthError;
            }

            snapshot["error"] = sitesCall.error ?? "Failed to fetch Omada sites.";
            if (!string.IsNullOrWhiteSpace(authError))
            {
                snapshot["error"] = snapshot["error"] + " Details: " + authError;
            }
            sources.Add(new Dictionary<string, object?>
            {
                ["endpoint"] = $"/openapi/v1/{OmadaOmadacId}/sites",
                ["status_code"] = sitesCall.statusCode,
                ["error"] = sitesCall.error,
            });
            return snapshot;
        }

        using (sitesCall.body)
        {
            var root = sitesCall.body.RootElement;
            var errorCode = TryGetOmadaErrorCode(root);
            if (errorCode.HasValue && errorCode.Value != 0)
            {
                var message = TryGetOmadaMessage(root) ?? "Omada API returned an error while loading sites.";
                snapshot["error"] = $"{message} (errorCode: {errorCode.Value})";
                sources.Add(new Dictionary<string, object?>
                {
                    ["endpoint"] = $"/openapi/v1/{OmadaOmadacId}/sites",
                    ["status_code"] = sitesCall.statusCode,
                    ["error_code"] = errorCode.Value,
                    ["error"] = message,
                });
                return snapshot;
            }

            if (!TryGetOmadaResult(root, out var sitesResult)
                || !sitesResult.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                snapshot["error"] = "Omada API did not return a valid site list payload.";
                return snapshot;
            }

            var sites = new List<Dictionary<string, object?>>();
            foreach (var item in data.EnumerateArray())
            {
                sites.Add(new Dictionary<string, object?>
                {
                    ["site_id"] = AppRequestHelpers.TryGetString(item, "siteId"),
                    ["name"] = AppRequestHelpers.TryGetString(item, "name"),
                    ["scenario"] = AppRequestHelpers.TryGetString(item, "scenario"),
                    ["region"] = AppRequestHelpers.TryGetString(item, "region"),
                    ["time_zone"] = AppRequestHelpers.TryGetString(item, "timeZone"),
                });
            }

            snapshot["sites"] = sites;

            var selectedSite = ResolveSelectedSite(sites);
            snapshot["selected_site"] = selectedSite;

            if (selectedSite is not null
                && selectedSite.TryGetValue("site_id", out var siteIdObj)
                && siteIdObj is string siteId
                && !string.IsNullOrWhiteSpace(siteId))
            {
                var devices = await TryFetchOmadaTotalRowsAsync(new[]
                {
                    $"/openapi/v1/{OmadaOmadacId}/sites/{siteId}/devices?page=1&pageSize=1",
                    $"/openapi/v1/{OmadaOmadacId}/sites/{siteId}/devices?currentPage=1&currentSize=1",
                });
                if (devices.totalRows.HasValue)
                {
                    snapshot["device_count_total"] = devices.totalRows.Value;
                }

                if (devices.source is not null)
                {
                    sources.Add(devices.source);
                }

                var clients = await TryFetchOmadaTotalRowsAsync(new[]
                {
                    $"/openapi/v1/{OmadaOmadacId}/sites/{siteId}/clients?page=1&pageSize=1",
                    $"/openapi/v1/{OmadaOmadacId}/sites/{siteId}/clients?currentPage=1&currentSize=1",
                });
                if (clients.totalRows.HasValue)
                {
                    snapshot["client_count_total"] = clients.totalRows.Value;
                }

                if (clients.source is not null)
                {
                    sources.Add(clients.source);
                }
            }
        }

        snapshot["connected"] = true;
        return snapshot;
    }

    private async Task<string?> EnsureOmadaAccessTokenAsync(bool forceRefresh = false)
    {
        lock (_omadaLock)
        {
            if (!forceRefresh
                && !string.IsNullOrWhiteSpace(_omadaAccessToken)
                && _omadaAccessTokenExpiryUtc > DateTimeOffset.UtcNow.AddSeconds(120))
            {
                return _omadaAccessToken;
            }
        }

        var attempts = new List<(string url, object body, string label)>
        {
            (
                BuildOmadaUrl("/openapi/authorize/token?grant_type=client_credentials"),
                new
                {
                    omadacId = OmadaOmadacId,
                    client_id = OmadaClientId,
                    client_secret = OmadaClientSecret,
                },
                "body.omadacId"
            ),
            (
                BuildOmadaUrl("/openapi/authorize/token?grant_type=client_credentials"),
                new
                {
                    omadac_id = OmadaOmadacId,
                    client_id = OmadaClientId,
                    client_secret = OmadaClientSecret,
                },
                "body.omadac_id"
            ),
            (
                BuildOmadaUrl("/openapi/authorize/token?grant_type=client_credentials&client_id="
                    + Uri.EscapeDataString(OmadaClientId)
                    + "&client_secret="
                    + Uri.EscapeDataString(OmadaClientSecret)),
                new
                {
                    omadacId = OmadaOmadacId,
                },
                "query.client_id+secret"
            ),
            (
                BuildOmadaUrl("/openapi/authorize/token?grant_type=client_credentials"),
                new
                {
                    omadacId = OmadaOmadacId,
                    clientId = OmadaClientId,
                    clientSecret = OmadaClientSecret,
                },
                "body.clientId+clientSecret"
            ),
        };

        string? lastError = null;
        foreach (var attempt in attempts)
        {
            var bodyJson = JsonSerializer.Serialize(attempt.body);
            using var request = new HttpRequestMessage(HttpMethod.Post, attempt.url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _unraidClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                lastError = $"{attempt.label}: HTTP {(int)response.StatusCode}";
                continue;
            }

            JsonDocument tokenDoc;
            try
            {
                tokenDoc = JsonDocument.Parse(payload);
            }
            catch
            {
                lastError = $"{attempt.label}: token response was not valid JSON";
                continue;
            }

            using (tokenDoc)
            {
                var root = tokenDoc.RootElement;
                var errorCode = TryGetOmadaErrorCode(root);
                if (errorCode.HasValue && errorCode.Value != 0)
                {
                    var msg = TryGetOmadaMessage(root) ?? "Unknown token error";
                    lastError = $"{attempt.label}: {msg} (errorCode {errorCode.Value})";
                    continue;
                }

                if (!TryGetOmadaResult(root, out var result))
                {
                    lastError = $"{attempt.label}: missing result field";
                    continue;
                }

                var token = AppRequestHelpers.TryGetString(result, "accessToken");
                var expiresIn = AppRequestHelpers.TryGetDouble(result, "expiresIn") ?? 7200;
                if (string.IsNullOrWhiteSpace(token))
                {
                    lastError = $"{attempt.label}: missing accessToken";
                    continue;
                }

                lock (_omadaLock)
                {
                    _omadaAccessToken = token;
                    _omadaAccessTokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));
                    _omadaLastAuthError = string.Empty;
                }

                return token;
            }
        }

        lock (_omadaLock)
        {
            _omadaLastAuthError = lastError ?? "Unknown token failure";
        }
        _logger.LogWarning("Omada token request failed after fallbacks: {Error}", lastError);
        return null;
    }

    private async Task<(bool success, JsonDocument? body, int? statusCode, string? error)> SendOmadaRequestAsync(
        HttpMethod method,
        string relativePath,
        bool retryAuth)
    {
        var token = await EnsureOmadaAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, null, null, "Unable to acquire Omada access token.");
        }

        var firstAttempt = await SendOmadaRequestWithTokenAsync(method, relativePath, token);
        if (firstAttempt.success)
        {
            return firstAttempt;
        }

        if (!retryAuth)
        {
            return firstAttempt;
        }

        if (!ShouldRetryOmadaAuth(firstAttempt.statusCode, firstAttempt.body))
        {
            return firstAttempt;
        }

        var refreshedToken = await EnsureOmadaAccessTokenAsync(forceRefresh: true);
        if (string.IsNullOrWhiteSpace(refreshedToken))
        {
            return firstAttempt;
        }

        return await SendOmadaRequestWithTokenAsync(method, relativePath, refreshedToken);
    }

    private async Task<(bool success, JsonDocument? body, int? statusCode, string? error)> SendOmadaRequestWithTokenAsync(
        HttpMethod method,
        string relativePath,
        string token)
    {
        var url = BuildOmadaUrl(relativePath);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Authorization", "AccessToken=" + token);

        using var response = await _unraidClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        JsonDocument? bodyDoc = null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                bodyDoc = JsonDocument.Parse(payload);
            }
            catch
            {
                bodyDoc = null;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorText = string.IsNullOrWhiteSpace(payload)
                ? $"HTTP {(int)response.StatusCode}"
                : payload[..Math.Min(payload.Length, 280)];
            return (false, bodyDoc, (int)response.StatusCode, errorText);
        }

        return (true, bodyDoc, (int)response.StatusCode, null);
    }

    private async Task<(int? totalRows, Dictionary<string, object?>? source)> TryFetchOmadaTotalRowsAsync(IEnumerable<string> candidates)
    {
        foreach (var path in candidates)
        {
            var call = await SendOmadaRequestAsync(HttpMethod.Get, path, retryAuth: true);
            if (!call.success || call.body is null)
            {
                continue;
            }

            using (call.body)
            {
                var root = call.body.RootElement;
                var errorCode = TryGetOmadaErrorCode(root);
                if (errorCode.HasValue && errorCode.Value != 0)
                {
                    continue;
                }

                if (!TryGetOmadaResult(root, out var result) || result.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var totalRows = AppRequestHelpers.TryGetDouble(result, "totalRows");
                if (!totalRows.HasValue)
                {
                    continue;
                }

                return ((int)totalRows.Value, new Dictionary<string, object?>
                {
                    ["endpoint"] = path,
                    ["status_code"] = call.statusCode,
                    ["total_rows"] = (int)totalRows.Value,
                });
            }
        }

        return (null, null);
    }

    private static bool ShouldRetryOmadaAuth(int? httpStatusCode, JsonDocument? body)
    {
        if (httpStatusCode == (int)HttpStatusCode.Unauthorized)
        {
            return true;
        }

        if (body is null)
        {
            return false;
        }

        var errorCode = TryGetOmadaErrorCode(body.RootElement);
        return errorCode == -44112 || errorCode == -44113;
    }

    private Dictionary<string, object?>? ResolveSelectedSite(List<Dictionary<string, object?>> sites)
    {
        if (sites.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(OmadaSiteId))
        {
            var matched = sites.FirstOrDefault(site =>
                site.TryGetValue("site_id", out var siteIdObj)
                && siteIdObj is string siteId
                && string.Equals(siteId, OmadaSiteId, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched;
            }
        }

        return sites[0];
    }

    private string BuildOmadaUrl(string relativePath)
    {
        var normalizedPath = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        return OmadaBaseUrl.TrimEnd('/') + normalizedPath;
    }

    private static bool TryGetOmadaResult(JsonElement root, out JsonElement result)
    {
        if (root.TryGetProperty("result", out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    private static int? TryGetOmadaErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("errorCode", out var node))
        {
            return null;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value))
        {
            return value;
        }

        if (node.ValueKind == JsonValueKind.String && int.TryParse(node.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? TryGetOmadaMessage(JsonElement root)
    {
        return root.TryGetProperty("msg", out var messageNode) ? messageNode.GetString() : null;
    }

    public async Task<Dictionary<string, object?>> FetchOmadaDetailAsync(string? requestedSiteId = null)
    {
        var fetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var detail = new Dictionary<string, object?>
        {
            ["fetched_at"] = fetchedAt,
            ["connected"] = false,
            ["error"] = null,
            ["selected_site"] = null,
            ["devices"] = new List<object>(),
            ["clients"] = new List<object>(),
        };

        var snapshot = GetOmadaSnapshot();

        if (snapshot.TryGetValue("connected", out var connObj) && connObj is bool conn && !conn)
        {
            var snapErr = snapshot.TryGetValue("error", out var eObj) && eObj is string errStr ? errStr : null;
            detail["error"] = snapErr ?? "Omada is not connected. Try refreshing the overview first.";
            return detail;
        }

        Dictionary<string, object?>? selectedSite = null;
        if (!string.IsNullOrWhiteSpace(requestedSiteId)
            && snapshot.TryGetValue("sites", out var sitesObj)
            && sitesObj is List<Dictionary<string, object?>> sites)
        {
            selectedSite = sites.FirstOrDefault(site =>
                site.TryGetValue("site_id", out var siteIdObj)
                && siteIdObj is string candidate
                && string.Equals(candidate, requestedSiteId, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedSite is null)
        {
            if (!snapshot.TryGetValue("selected_site", out var selectedSiteObj)
                || selectedSiteObj is not Dictionary<string, object?> selectedSiteFromSnapshot)
            {
                detail["error"] = "No active Omada site is selected.";
                return detail;
            }

            selectedSite = selectedSiteFromSnapshot;
        }

        detail["selected_site"] = selectedSite;

        if (!selectedSite.TryGetValue("site_id", out var siteIdObj)
            || siteIdObj is not string selectedSiteId
            || string.IsNullOrWhiteSpace(selectedSiteId))
        {
            detail["error"] = "Selected site has no valid site_id.";
            return detail;
        }

        var devicesCall = await SendOmadaRequestAsync(
            HttpMethod.Get,
            $"/openapi/v1/{OmadaOmadacId}/sites/{selectedSiteId}/devices?page=1&pageSize=200",
            retryAuth: true);

        if (devicesCall.success && devicesCall.body is not null)
        {
            using (devicesCall.body)
            {
                var root = devicesCall.body.RootElement;
                var errorCode = TryGetOmadaErrorCode(root);
                if ((!errorCode.HasValue || errorCode.Value == 0)
                    && TryGetOmadaResult(root, out var result)
                    && result.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array)
                {
                    var devices = new List<object>();
                    foreach (var item in data.EnumerateArray())
                    {
                        devices.Add(ParseOmadaDevice(item));
                    }
                    detail["devices"] = devices;
                }
            }
        }

        var clientsCall = await SendOmadaRequestAsync(
            HttpMethod.Get,
            $"/openapi/v1/{OmadaOmadacId}/sites/{selectedSiteId}/clients?page=1&pageSize=200",
            retryAuth: true);

        if (clientsCall.success && clientsCall.body is not null)
        {
            using (clientsCall.body)
            {
                var root = clientsCall.body.RootElement;
                var errorCode = TryGetOmadaErrorCode(root);
                if ((!errorCode.HasValue || errorCode.Value == 0)
                    && TryGetOmadaResult(root, out var result)
                    && result.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array)
                {
                    var clients = new List<object>();
                    foreach (var item in data.EnumerateArray())
                    {
                        clients.Add(ParseOmadaClient(item));
                    }
                    detail["clients"] = clients;
                }
            }
        }

        detail["connected"] = true;
        return detail;
    }

    private static Dictionary<string, object?> ParseOmadaDevice(JsonElement item)
    {
        return new Dictionary<string, object?>
        {
            ["mac"] = AppRequestHelpers.TryGetString(item, "mac"),
            ["name"] = AppRequestHelpers.TryGetString(item, "name"),
            ["type"] = AppRequestHelpers.TryGetString(item, "type"),
            ["ip"] = AppRequestHelpers.TryGetString(item, "ip"),
            ["model"] = AppRequestHelpers.TryGetString(item, "model"),
            ["firmware_version"] = AppRequestHelpers.TryGetString(item, "firmwareVersion"),
            ["status"] = AppRequestHelpers.TryGetDouble(item, "status"),
            ["uptime"] = AppRequestHelpers.TryGetDouble(item, "uptime"),
            ["clients"] = AppRequestHelpers.TryGetDouble(item, "clientNum") ?? AppRequestHelpers.TryGetDouble(item, "clients"),
            ["download"] = AppRequestHelpers.TryGetDouble(item, "download"),
            ["upload"] = AppRequestHelpers.TryGetDouble(item, "upload"),
        };
    }

    private static Dictionary<string, object?> ParseOmadaClient(JsonElement item)
    {
        bool? wireless = null;
        if (item.TryGetProperty("wireless", out var wProp))
        {
            wireless = wProp.ValueKind == JsonValueKind.True;
        }

        bool? active = null;
        if (item.TryGetProperty("active", out var activeProp))
        {
            active = activeProp.ValueKind == JsonValueKind.True;
        }

        return new Dictionary<string, object?>
        {
            ["mac"] = AppRequestHelpers.TryGetString(item, "mac"),
            ["name"] = AppRequestHelpers.TryGetString(item, "name"),
            ["ip"] = AppRequestHelpers.TryGetString(item, "ip"),
            ["network_name"] = AppRequestHelpers.TryGetString(item, "networkName"),
            ["ssid"] = AppRequestHelpers.TryGetString(item, "ssid"),
            ["wireless"] = wireless,
            ["signal_level"] = TryGetFirstDoubleDeep(item, "signalLevel", "rssi", "signal"),
            ["rx_rate"] = TryGetFirstDoubleDeep(item, "rxRate", "download", "downRate", "rx"),
            ["tx_rate"] = TryGetFirstDoubleDeep(item, "txRate", "upload", "upRate", "tx"),
            ["wired_link_speed"] = TryGetFirstLinkSpeedMbps(item, "wiredLinkSpeed", "linkSpeed", "portSpeed", "speed", "rate", "wiredRate", "linkRate", "portRate", "negotiatedRate", "speedMbps", "connectionSpeed", "duplexSpeed"),
            ["wired_link_speed_text"] = TryGetFirstStringDeep(item, "wiredLinkSpeed", "linkSpeed", "portSpeed", "speed", "rate", "wiredRate", "linkRate", "portRate", "negotiatedRate", "speedMbps", "connectionSpeed", "duplexSpeed"),
            ["uptime"] = AppRequestHelpers.TryGetDouble(item, "uptime"),
            ["active"] = active,
        };
    }

    private static readonly Regex LeadingNumeric = new(@"([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

    private static double? TryGetFirstLinkSpeedMbps(JsonElement root, params string[] propertyNames)
    {
        var numeric = TryGetFirstDoubleDeep(root, propertyNames);
        if (numeric.HasValue)
        {
            return numeric.Value;
        }

        var text = TryGetFirstStringDeep(root, propertyNames);
        if (TryParseLinkSpeedTextToMbps(text, out var parsedMbps))
        {
            return parsedMbps;
        }

        return TryInferLikelyLinkSpeedMbps(root);
    }

    private static double? TryInferLikelyLinkSpeedMbps(JsonElement root)
    {
        var candidates = new List<double>();
        foreach (var (name, value) in EnumerateNamedValuesRecursive(root, depth: 0))
        {
            var normalized = NormalizePropertyName(name);
            if (!LooksLikeLinkSpeedKey(normalized))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var direct) && direct > 0)
            {
                candidates.Add(direct);
                continue;
            }

            if (value.ValueKind == JsonValueKind.String && TryParseLinkSpeedTextToMbps(value.GetString(), out var parsed))
            {
                candidates.Add(parsed);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // Prefer the highest negotiated speed if multiple candidates exist.
        return candidates.Max();
    }

    private static bool LooksLikeLinkSpeedKey(string normalizedName)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        var hasSpeedHint = normalizedName.Contains("speed", StringComparison.Ordinal)
            || normalizedName.Contains("linkspeed", StringComparison.Ordinal)
            || normalizedName.Contains("portspeed", StringComparison.Ordinal)
            || normalizedName.Contains("duplex", StringComparison.Ordinal)
            || normalizedName.Contains("negotiat", StringComparison.Ordinal);

        if (!hasSpeedHint)
        {
            return false;
        }

        return !normalizedName.Contains("signal", StringComparison.Ordinal)
            && !normalizedName.Contains("upload", StringComparison.Ordinal)
            && !normalizedName.Contains("download", StringComparison.Ordinal)
            && !normalizedName.StartsWith("rx", StringComparison.Ordinal)
            && !normalizedName.StartsWith("tx", StringComparison.Ordinal);
    }

    private static double? TryGetFirstDoubleDeep(JsonElement root, params string[] propertyNames)
    {
        foreach (var value in EnumerateMatchingPropertyValues(root, propertyNames))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var direct))
            {
                return direct;
            }

            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? TryGetFirstStringDeep(JsonElement root, params string[] propertyNames)
    {
        foreach (var value in EnumerateMatchingPropertyValues(root, propertyNames))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateMatchingPropertyValues(JsonElement root, params string[] propertyNames)
    {
        if (propertyNames is null || propertyNames.Length == 0)
        {
            yield break;
        }

        var expected = propertyNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => NormalizePropertyName(name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (name, value) in EnumerateNamedValuesRecursive(root, depth: 0))
        {
            if (expected.Contains(NormalizePropertyName(name)))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<(string name, JsonElement value)> EnumerateNamedValuesRecursive(JsonElement element, int depth)
    {
        if (depth > 8)
        {
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return (property.Name, property.Value);
                foreach (var nested in EnumerateNamedValuesRecursive(property.Value, depth + 1))
                {
                    yield return nested;
                }
            }
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateNamedValuesRecursive(item, depth + 1))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string NormalizePropertyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var chars = name.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private static bool TryParseLinkSpeedTextToMbps(string? text, out double mbps)
    {
        mbps = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        var match = LeadingNumeric.Match(normalized);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var value) || value <= 0)
        {
            return false;
        }

        if (normalized.Contains("gbps") || normalized.Contains("gbit") || normalized.EndsWith("g"))
        {
            mbps = value * 1000;
            return true;
        }

        if (normalized.Contains("mbps") || normalized.Contains("mbit") || normalized.EndsWith("m"))
        {
            mbps = value;
            return true;
        }

        if (normalized.Contains("kbps") || normalized.Contains("kbit") || normalized.EndsWith("k"))
        {
            mbps = value / 1000;
            return true;
        }

        if (normalized.Contains("bps") || normalized.Contains("bit/s"))
        {
            mbps = value / 1_000_000;
            return true;
        }

        // No unit present, assume Omada is reporting Mbps.
        mbps = value;
        return true;
    }

    private static double? TryGetFirstDouble(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = AppRequestHelpers.TryGetDouble(root, propertyName);
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    private static string? TryGetFirstString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = AppRequestHelpers.TryGetString(root, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
