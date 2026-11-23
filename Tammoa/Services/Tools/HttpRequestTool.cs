using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services.Tools;

public class HttpRequestTool : ITool
{
    private static readonly HashSet<string> Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"];
    private readonly IHttpRequestRunner _http;

    public HttpRequestTool(IHttpRequestRunner http) => _http = http;

    public ToolDefinition Definition { get; } = new(ToolIds.HttpRequest,
        "Sends an HTTP request to one of the allowed hosts (to fetch a web page or call an API) and returns the status and the response text. Redirects are not followed.",
        ToolArgs.Schema(new JObject
        {
            ["url"] = ToolArgs.Prop("Full http(s) URL."),
            ["method"] = new JObject { ["type"] = "string", ["enum"] = new JArray(Methods.ToArray()), ["description"] = "GET (default), POST, PUT, PATCH, DELETE or HEAD." },
            ["headers"] = ToolArgs.Prop("Optional headers as an object of name: value.", "object"),
            ["body"] = ToolArgs.Prop("Optional request body text."),
        }, "url"));

    public bool NeedsRoot => false;

    public void Check(JObject arguments, ToolContext context) => Parse(arguments, context);

    public async Task<string> RunAsync(JObject arguments, ToolContext context, CancellationToken ct)
    {
        var (method, url) = Parse(arguments, context);
        var headers = new Dictionary<string, string>();
        if (arguments["headers"] is JObject h) foreach (var p in h.Properties()) headers[p.Name] = (string?)p.Value ?? "";
        var body = arguments["body"] is { Type: not JTokenType.Null } b ? (b.Type == JTokenType.String ? (string)b! : b.ToString(Newtonsoft.Json.Formatting.None)) : null;
        var r = await _http.SendAsync(method, url, headers, body, TimeSpan.FromSeconds(30), ct, followRedirects: false);
        var head = $"HTTP {r.Status}" + (r.Status is >= 300 and < 400 && r.Headers.TryGetValue("location", out var loc) ? $" (redirect to {loc}, not followed; call again if that host is allowed)" : "");
        return head + "\n" + ToolArgs.Limit(r.Text, 20_000) + (r.Truncated ? "\n[the response was cut]" : "");
    }

    private static (string Method, Uri Url) Parse(JObject a, ToolContext c)
    {
        var method = ToolArgs.Optional(a, "method", "GET").ToUpperInvariant();
        if (!Methods.Contains(method)) throw new ArgumentException($"Unknown method '{method}'.");
        if (!Uri.TryCreate(ToolArgs.Required(a, "url"), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new ArgumentException("Give a full http:// or https:// URL.");
        if (c.AllowedHosts.Count == 0) throw new ToolRejectedException("HTTP requests are switched off: this card has no allowed hosts.");
        if (!c.AllowedHosts.Any(h => HostMatches(h, url.Host)))
            throw new ToolRejectedException($"The host '{url.Host}' is not allowed. Allowed hosts: {string.Join(", ", c.AllowedHosts)}.");
        return (method, url);
    }

    /// <summary>Exact host name, or "*.example.com" for any sub-domain of example.com.</summary>
    public static bool HostMatches(string allowed, string host)
    {
        allowed = allowed.Trim().ToLowerInvariant();
        host = host.ToLowerInvariant();
        return allowed.StartsWith("*.") ? host.EndsWith(allowed[1..]) : host == allowed;
    }
}
