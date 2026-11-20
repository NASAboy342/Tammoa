using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tammoa.Services;

public class TemplateEngine : ITemplateEngine
{
    private static readonly Regex Expr = new(@"\{\{\s*(.*?)\s*\}\}", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PathPart = new(@"([^.\[\]]+)|\[(\d+)\]", RegexOptions.Compiled);

    public string Render(string template, JToken context) =>
        Expr.Replace(template, m => Evaluate(m.Groups[1].Value, context));

    public JToken Resolve(JToken template, JToken context) => template switch
    {
        JValue { Type: JTokenType.String } v => ResolveString((string)v!, context),
        JObject o => new JObject(o.Properties().Select(p => new JProperty(p.Name, Resolve(p.Value, context)))),
        JArray a => new JArray(a.Select(i => Resolve(i, context))),
        _ => template.DeepClone(),
    };

    private JToken ResolveString(string s, JToken context)
    {
        var m = Expr.Match(s);
        if (m.Success && m.Index == 0 && m.Length == s.Length)
        {
            var (path, filters) = Parse(m.Groups[1].Value);
            if (filters.Count == 0) return Lookup(context, path)?.DeepClone() ?? JValue.CreateNull();
        }
        return new JValue(Render(s, context));
    }

    private static string Evaluate(string expression, JToken context)
    {
        var (path, filters) = Parse(expression);
        var token = Lookup(context, path);
        string? current = null;
        foreach (var filter in filters)
        {
            switch (filter)
            {
                case "json": current = (token ?? JValue.CreateNull()).ToString(Formatting.None); break;
                case "sh": current = ShellQuote(current ?? AsText(token)); break;
                default: throw new ArgumentException($"Unknown template filter '{filter}' (use json or sh).");
            }
        }
        return current ?? AsText(token);
    }

    private static (string Path, List<string> Filters) Parse(string expression)
    {
        var parts = expression.Split('|').Select(p => p.Trim()).ToList();
        return (parts[0], parts.Skip(1).Where(f => f.Length > 0).ToList());
    }

    private static JToken? Lookup(JToken context, string path)
    {
        JToken? current = context;
        foreach (Match m in PathPart.Matches(path))
        {
            if (current is null) return null;
            if (m.Groups[1].Success) current = current is JObject o ? o[m.Groups[1].Value] : null;
            else current = current is JArray a && int.Parse(m.Groups[2].Value) is var i && i < a.Count ? a[i] : null;
        }
        return current;
    }

    private static string AsText(JToken? t) => t switch
    {
        null => "",
        { Type: JTokenType.Null } => "",
        JValue { Type: JTokenType.String } v => (string)v!,
        _ => t.ToString(Formatting.None),
    };

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
