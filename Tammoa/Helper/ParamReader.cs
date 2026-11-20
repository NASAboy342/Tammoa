using Newtonsoft.Json.Linq;

namespace Tammoa.Helper;

/// <summary>Tolerant reads of card params (the UI may send "30" or 30, "" for empty).</summary>
public static class ParamReader
{
    public static string GetString(JObject p, string key) =>
        p[key] is { Type: not JTokenType.Null } t ? (t.Type == JTokenType.String ? (string)t! : t.ToString(Newtonsoft.Json.Formatting.None)) : "";

    public static int GetInt(JObject p, string key, int fallback)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null) return fallback;
        if (t.Type is JTokenType.Integer or JTokenType.Float) return (int)t;
        return int.TryParse((string?)t, out var n) ? n : fallback;
    }

    public static bool GetBool(JObject p, string key, bool fallback)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null) return fallback;
        if (t.Type == JTokenType.Boolean) return (bool)t;
        return bool.TryParse((string?)t, out var b) ? b : fallback;
    }
}
