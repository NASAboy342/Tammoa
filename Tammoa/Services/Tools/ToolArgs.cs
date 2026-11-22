using Newtonsoft.Json.Linq;

namespace Tammoa.Services.Tools;

internal static class ToolArgs
{
    /// <summary>A required text argument. Small models sometimes send numbers or objects: they are turned into text.</summary>
    public static string Required(JObject a, string key)
    {
        var t = a[key];
        if (t is null || t.Type == JTokenType.Null) throw new ArgumentException($"Missing argument '{key}'.");
        var s = t.Type == JTokenType.String ? (string)t! : t.ToString(Newtonsoft.Json.Formatting.None);
        if (s.Length == 0) throw new ArgumentException($"Argument '{key}' is empty.");
        return s;
    }

    public static string Optional(JObject a, string key, string fallback) =>
        a[key] is { Type: not JTokenType.Null } t && (t.Type == JTokenType.String ? (string)t! : t.ToString(Newtonsoft.Json.Formatting.None)) is { Length: > 0 } s ? s : fallback;

    public static string Limit(string text, int max) => text.Length <= max ? text : text[..max] + $"\n[cut: {text.Length - max} more characters]";

    public static JObject Schema(JObject properties, params string[] required) =>
        new() { ["type"] = "object", ["properties"] = properties, ["required"] = new JArray(required) };

    public static JObject Prop(string description, string type = "string") => new() { ["type"] = type, ["description"] = description };

    public static string RequireRoot(ToolContext c) =>
        string.IsNullOrWhiteSpace(c.Root) ? throw new ToolRejectedException("No root folder is set for the tools of this card.") : c.Root;
}
