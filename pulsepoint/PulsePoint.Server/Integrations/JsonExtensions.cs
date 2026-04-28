using System.Text.Json;

namespace PulsePoint.Integrations;

internal static class JsonExtensions
{
    public static string GetStringOrEmpty(this JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    public static int GetInt32Safe(this JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.TryGetInt32(out var i) ? i : 0,
                JsonValueKind.String => int.TryParse(el.GetString(), out var si) ? si : 0,
                _ => 0
            };
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : 0,
            JsonValueKind.String => int.TryParse(v.GetString(), out var si) ? si : 0,
            _ => 0
        };
    }

    public static long GetInt64Safe(this JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.TryGetInt64(out var i) ? i : 0,
                JsonValueKind.String => long.TryParse(el.GetString(), out var si) ? si : 0,
                _ => 0
            };
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var i) ? i : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), out var si) ? si : 0,
            _ => 0
        };
    }

    public static double GetDoubleSafe(this JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.TryGetDouble(out var d) ? d : 0,
                JsonValueKind.String => double.TryParse(el.GetString(), out var sd) ? sd : 0,
                _ => 0
            };
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDouble(out var d) ? d : 0,
            JsonValueKind.String => double.TryParse(v.GetString(), out var sd) ? sd : 0,
            _ => 0
        };
    }

    public static double? GetDoubleNullable(this JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            if (el.ValueKind == JsonValueKind.Null) return null;
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.TryGetDouble(out var d) ? d : null,
                JsonValueKind.String => double.TryParse(el.GetString(), out var sd) ? sd : null,
                _ => null
            };
        }
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String => double.TryParse(v.GetString(), out var sd) ? sd : null,
            _ => null
        };
    }

    public static string GetNestedStatus(this JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        if (el.TryGetProperty("Status", out var s))
        {
            if (s.ValueKind == JsonValueKind.String) return s.GetString() ?? "";
            if (s.ValueKind == JsonValueKind.Object)
            {
                if (s.TryGetProperty("Health", out var h)) return h.GetString() ?? "";
                if (s.TryGetProperty("State", out var st)) return st.GetString() ?? "";
            }
        }
        return "";
    }
}
