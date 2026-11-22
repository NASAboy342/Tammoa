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

    public static double GetDouble(JObject p, string key, double fallback)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null) return fallback;
        if (t.Type is JTokenType.Integer or JTokenType.Float) return (double)t;
        return double.TryParse((string?)t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    /// <summary>An optional number: null when the param is missing, null or an empty string.</summary>
    public static double? GetOptionalDouble(JObject p, string key)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null || (t.Type == JTokenType.String && string.IsNullOrWhiteSpace((string?)t))) return null;
        return GetDouble(p, key, double.NaN) is var d && !double.IsNaN(d) ? d : throw new ArgumentException($"'{key}' must be a number.");
    }

    public static bool GetBool(JObject p, string key, bool fallback)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null) return fallback;
        if (t.Type == JTokenType.Boolean) return (bool)t;
        return bool.TryParse((string?)t, out var b) ? b : fallback;
    }

    /// <summary>A list of strings: a JSON array, or a comma/newline separated text. Empty entries are dropped, duplicates removed.</summary>
    public static IReadOnlyList<string> GetStringList(JObject p, string key)
    {
        var t = p[key];
        if (t is null || t.Type == JTokenType.Null) return [];
        IEnumerable<string> items = t is JArray a
            ? a.Select(x => x.Type == JTokenType.String ? (string)x! : x.ToString(Newtonsoft.Json.Formatting.None))
            : ((string?)t ?? "").Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries);
        return items.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
    }
}
