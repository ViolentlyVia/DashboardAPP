using System.Net.NetworkInformation;
using System.Text.Json;

namespace SysDash.NetCore.Backend;

public static class AppRequestHelpers
{
    public static bool RequireKey(HttpContext context, string requiredKey, out IResult denied)
    {
        var key = context.Request.Query["key"].ToString();
        if (key != requiredKey)
        {
            denied = Results.Json(new { error = "Access denied: invalid or missing key" }, statusCode: 401);
            return false;
        }

        denied = Results.Empty;
        return true;
    }

    public static async Task<double?> TryPingMillisecondsAsync(string ip, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                return Math.Round((double)reply.RoundtripTime, 2);
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    public static string? TryGetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => value.GetRawText(),
        };
    }

    public static double? TryGetDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d))
        {
            return d;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
