using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SysDash.NetCore.Backend;

public sealed partial class AppState
{
    public Dictionary<string, object?> GetIdracSnapshot()
    {
        lock (_idracLock)
        {
            return new Dictionary<string, object?>(_idracSnapshot);
        }
    }

    public void SetIdracSnapshot(Dictionary<string, object?> snapshot)
    {
        lock (_idracLock)
        {
            _idracSnapshot = snapshot;
        }
    }

    public async Task<Dictionary<string, object?>> FetchIdracSummaryAsync()
    {
        var fetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = new Dictionary<string, object?>
        {
            ["fetched_at"] = fetchedAt,
            ["host"] = IdracHost,
            ["connected"] = false,
            ["error"] = null,
            ["system"] = null,
            ["manager"] = null,
            ["thermal"] = null,
            ["power"] = null,
            ["disks"] = new List<object>(),
        };

        try
        {
            using var rootDoc = await FetchIdracJsonAsync("/redfish/v1");
            if (rootDoc is null)
            {
                result["error"] = "Unable to reach iDRAC Redfish service root.";
                return result;
            }

            var systemsCollectionPath = GetNestedString(rootDoc.RootElement, "Systems", "@odata.id") ?? "/redfish/v1/Systems";
            var managersCollectionPath = GetNestedString(rootDoc.RootElement, "Managers", "@odata.id") ?? "/redfish/v1/Managers";
            var chassisCollectionPath = GetNestedString(rootDoc.RootElement, "Chassis", "@odata.id") ?? "/redfish/v1/Chassis";

            var systemPath = await ResolveFirstMemberPathAsync(systemsCollectionPath);
            var managerPath = await ResolveFirstMemberPathAsync(managersCollectionPath);
            var chassisPath = await ResolveFirstMemberPathAsync(chassisCollectionPath);

            result["connected"] = true;
            result["paths"] = new Dictionary<string, object?>
            {
                ["system"] = systemPath,
                ["manager"] = managerPath,
                ["chassis"] = chassisPath,
            };

            if (!string.IsNullOrWhiteSpace(systemPath))
            {
                using var systemDoc = await FetchIdracJsonAsync(systemPath);
                if (systemDoc is not null)
                {
                    var root = systemDoc.RootElement;
                    result["system"] = new Dictionary<string, object?>
                    {
                        ["name"] = GetString(root, "Name"),
                        ["manufacturer"] = GetString(root, "Manufacturer"),
                        ["model"] = GetString(root, "Model"),
                        ["service_tag"] = GetString(root, "SKU") ?? GetString(root, "SerialNumber"),
                        ["power_state"] = GetString(root, "PowerState"),
                        ["bios_version"] = GetString(root, "BiosVersion"),
                        ["health"] = GetNestedString(root, "Status", "Health"),
                        ["health_rollup"] = GetNestedString(root, "Status", "HealthRollup"),
                        ["hostname"] = GetString(root, "HostName"),
                    };

                    result["disks"] = await ParseDisksAsync(root);
                }
            }

            if (!string.IsNullOrWhiteSpace(managerPath))
            {
                using var managerDoc = await FetchIdracJsonAsync(managerPath);
                if (managerDoc is not null)
                {
                    var root = managerDoc.RootElement;
                    result["manager"] = new Dictionary<string, object?>
                    {
                        ["name"] = GetString(root, "Name"),
                        ["model"] = GetString(root, "Model"),
                        ["firmware_version"] = GetString(root, "FirmwareVersion"),
                        ["date_time"] = GetString(root, "DateTime"),
                        ["health"] = GetNestedString(root, "Status", "Health"),
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(chassisPath))
            {
                var thermalPath = chassisPath.TrimEnd('/') + "/Thermal";
                var powerPath = chassisPath.TrimEnd('/') + "/Power";

                using var thermalDoc = await FetchIdracJsonAsync(thermalPath);
                if (thermalDoc is not null)
                {
                    result["thermal"] = ParseThermal(thermalDoc.RootElement);
                }

                using var powerDoc = await FetchIdracJsonAsync(powerPath);
                if (powerDoc is not null)
                {
                    result["power"] = ParsePower(powerDoc.RootElement);
                }
            }
        }
        catch (Exception ex)
        {
            result["error"] = ex.Message;
        }

        return result;
    }

    private async Task<JsonDocument?> FetchIdracJsonAsync(string path)
    {
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        var url = "https://" + IdracHost + normalizedPath;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var rawAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes(IdracUsername + ":" + IdracPassword));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", rawAuth);
        req.Headers.Accept.ParseAdd("application/json");

        using var res = await _unraidClient.SendAsync(req);
        var content = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("iDRAC request failed: {StatusCode} {Url}", (int)res.StatusCode, url);
            return null;
        }

        return JsonDocument.Parse(content);
    }

    private async Task<string?> ResolveFirstMemberPathAsync(string? collectionPath)
    {
        if (string.IsNullOrWhiteSpace(collectionPath))
        {
            return null;
        }

        using var collectionDoc = await FetchIdracJsonAsync(collectionPath);
        if (collectionDoc is null)
        {
            return null;
        }

        if (collectionDoc.RootElement.TryGetProperty("Members", out var members)
            && members.ValueKind == JsonValueKind.Array
            && members.GetArrayLength() > 0)
        {
            var first = members[0];
            if (first.TryGetProperty("@odata.id", out var pathValue) && pathValue.ValueKind == JsonValueKind.String)
            {
                return pathValue.GetString();
            }
        }

        return collectionPath;
    }

    private static Dictionary<string, object?> ParseThermal(JsonElement root)
    {
        var cpuTemps = new List<double>();
        var fans = new List<Dictionary<string, object?>>();

        if (root.TryGetProperty("Temperatures", out var temps) && temps.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in temps.EnumerateArray())
            {
                var name = GetString(t, "Name") ?? string.Empty;
                var reading = GetNullableDouble(t, "ReadingCelsius");
                if (reading.HasValue && name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                {
                    cpuTemps.Add(reading.Value);
                }
            }
        }

        if (root.TryGetProperty("Fans", out var fanArray) && fanArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fanArray.EnumerateArray())
            {
                fans.Add(new Dictionary<string, object?>
                {
                    ["name"] = GetString(f, "Name"),
                    ["rpm"] = GetNullableDouble(f, "Reading") ?? GetNullableDouble(f, "ReadingRPM"),
                    ["units"] = GetString(f, "ReadingUnits"),
                    ["state"] = GetNestedString(f, "Status", "State"),
                    ["health"] = GetNestedString(f, "Status", "Health"),
                });
            }
        }

        var avg = cpuTemps.Count > 0 ? cpuTemps.Average() : (double?)null;
        return new Dictionary<string, object?>
        {
            ["cpu_temp_avg_c"] = avg,
            ["cpu_temps_c"] = cpuTemps,
            ["fans"] = fans,
        };
    }

    private static Dictionary<string, object?> ParsePower(JsonElement root)
    {
        var supplies = new List<Dictionary<string, object?>>();
        if (root.TryGetProperty("PowerSupplies", out var supplyArray) && supplyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in supplyArray.EnumerateArray())
            {
                supplies.Add(new Dictionary<string, object?>
                {
                    ["name"] = GetString(p, "Name") ?? GetString(p, "MemberId"),
                    ["model"] = GetString(p, "Model"),
                    ["state"] = GetNestedString(p, "Status", "State"),
                    ["health"] = GetNestedString(p, "Status", "Health"),
                    ["last_output_w"] = GetNullableDouble(p, "LastPowerOutputWatts"),
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["power_supplies"] = supplies,
        };
    }

    private async Task<List<Dictionary<string, object?>>> ParseDisksAsync(JsonElement systemRoot)
    {
        var disks = new List<Dictionary<string, object?>>();

        var storageCollectionPath = GetNestedString(systemRoot, "Storage", "@odata.id");
        if (!string.IsNullOrWhiteSpace(storageCollectionPath))
        {
            using var storageCollectionDoc = await FetchIdracJsonAsync(storageCollectionPath);
            if (storageCollectionDoc is not null
                && storageCollectionDoc.RootElement.TryGetProperty("Members", out var members)
                && members.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in members.EnumerateArray())
                {
                    var controllerPath = GetString(member, "@odata.id");
                    if (string.IsNullOrWhiteSpace(controllerPath))
                    {
                        continue;
                    }

                    using var controllerDoc = await FetchIdracJsonAsync(controllerPath);
                    if (controllerDoc is null)
                    {
                        continue;
                    }

                    if (!controllerDoc.RootElement.TryGetProperty("Drives", out var drivesNode)
                        || drivesNode.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var driveRef in drivesNode.EnumerateArray())
                    {
                        var drivePath = GetString(driveRef, "@odata.id");
                        if (string.IsNullOrWhiteSpace(drivePath))
                        {
                            continue;
                        }

                        using var driveDoc = await FetchIdracJsonAsync(drivePath);
                        if (driveDoc is null)
                        {
                            continue;
                        }

                        var drive = driveDoc.RootElement;
                        var allInfo = ConvertJsonElementToObject(drive);
                        disks.Add(new Dictionary<string, object?>
                        {
                            ["name"] = GetString(drive, "Name") ?? GetString(drive, "Id"),
                            ["model"] = GetString(drive, "Model"),
                            ["serial"] = GetString(drive, "SerialNumber"),
                            ["media_type"] = GetString(drive, "MediaType"),
                            ["protocol"] = GetString(drive, "Protocol"),
                            ["capacity_bytes"] = GetNullableDouble(drive, "CapacityBytes"),
                            ["state"] = GetNestedString(drive, "Status", "State"),
                            ["health"] = GetNestedString(drive, "Status", "Health"),
                            ["manufacturer"] = GetString(drive, "Manufacturer"),
                            ["controller_path"] = controllerPath,
                            ["drive_path"] = drivePath,
                            ["all_info"] = allInfo,
                            ["info_from_location"] = TrimObjectBeforeLocation(allInfo),
                        });
                    }
                }
            }
        }

        return disks;
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static double? GetNullableDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n))
        {
            return n;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? GetNestedString(JsonElement root, string parent, string child)
    {
        if (!root.TryGetProperty(parent, out var parentValue) || parentValue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!parentValue.TryGetProperty(child, out var childValue))
        {
            return null;
        }

        return childValue.ValueKind == JsonValueKind.String ? childValue.GetString() : childValue.ToString();
    }

    private static object? ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElementToObject(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElementToObject)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.TryGetDouble(out var d) ? d : element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static object? TrimObjectBeforeLocation(object? source)
    {
        if (source is not Dictionary<string, object?> dict)
        {
            return source;
        }

        var keys = dict.Keys.ToList();
        var locationIndex = keys.FindIndex(k => string.Equals(k, "Location", StringComparison.OrdinalIgnoreCase));
        if (locationIndex < 0)
        {
            return source;
        }

        var trimmed = new Dictionary<string, object?>();
        for (var i = locationIndex; i < keys.Count; i += 1)
        {
            var key = keys[i];
            trimmed[key] = dict[key];
        }

        return trimmed;
    }
}
