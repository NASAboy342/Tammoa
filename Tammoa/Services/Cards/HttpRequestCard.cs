using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class HttpRequestCard : ICardType
{
    public const string TypeId = "net.http";
    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"];
    private readonly ITemplateEngine _templates;
    private readonly IHttpRequestRunner _http;

    public HttpRequestCard(ITemplateEngine templates, IHttpRequestRunner http)
    {
        _templates = templates;
        _http = http;
    }

    public CardTypeInfo Info { get; } = new(
        TypeId, "HTTP Request",
        "Calls a web API. Output: { status, ok, headers, text, json? } (json when the response body is JSON). Templates work in the URL, headers and body.",
        CardCategory.Action, "globe",
        [
            new CardParamInfo("method", "Method", ParamKind.Select, Required: true, DefaultValue: "GET", Options: Methods.Select(m => new ParamOption(m, m)).ToList()),
            new CardParamInfo("url", "URL", ParamKind.Text, Required: true, Placeholder: "https://api.example.com/items"),
            new CardParamInfo("headers", "Headers (JSON object)", ParamKind.Code, CodeLanguage: "json", Placeholder: "{ \"Authorization\": \"Bearer …\" }",
                Help: "Do not paste secrets you will export: a flow file contains everything typed here."),
            new CardParamInfo("body", "Body", ParamKind.Code, CodeLanguage: "text", Help: "Sent for POST / PUT / PATCH / DELETE. JSON is detected automatically."),
            new CardParamInfo("timeoutSeconds", "Timeout (seconds)", ParamKind.Number, DefaultValue: 30, Help: "1 to 600."),
            new CardParamInfo("failOnError", "Fail on HTTP error (not 2xx)", ParamKind.Bool, DefaultValue: true),
        ])
    {
        Guide = new CardGuide(
            "Calls a web API and gives you the answer. Use it to fetch data, or to send data to another service.",
            "Receives the previous card's output as input. Put {{input.field}} in the URL, headers or body to use it; {{run.input}} is the Run input.",
            new JObject { ["status"] = 200, ["ok"] = true, ["headers"] = new JObject { ["content-type"] = "application/json" }, ["text"] = "{\"id\":1,\"name\":\"Sok\"}", ["truncated"] = false, ["json"] = new JObject { ["id"] = 1, ["name"] = "Sok" } },
            [
                new GuideExample("Fetch something using a value from the input", "The URL is filled in first, then the request is sent.",
                    new JObject { ["method"] = "GET", ["url"] = "https://api.example.com/users/{{input.json.id}}" }, "https://api.example.com/users/{{input.json.id}}", "text",
                    new JObject { ["json"] = new JObject { ["id"] = 7 } },
                    "Requests /users/7. If the answer is JSON the next card reads {{input.json.name}}; the raw answer is {{input.text}}; the HTTP code is {{input.status}}."),
                new GuideExample("Send JSON", "POST a body built from the input. JSON bodies are detected automatically.",
                    new JObject { ["method"] = "POST", ["url"] = "https://api.example.com/items", ["headers"] = "{ \"Authorization\": \"Bearer YOUR_TOKEN\" }", ["body"] = "{ \"name\": \"{{input.json.name}}\" }" },
                    "{ \"name\": \"{{input.json.name}}\" }", "json", new JObject { ["json"] = new JObject { ["name"] = "Sok" } },
                    "Sends {\"name\":\"Sok\"}. Replace YOUR_TOKEN with a real key (and remember a flow file contains what you type here)."),
            ],
            ["Output fields: status, ok, text (the raw answer), json (when the answer is JSON), headers.", "Any non-2xx answer fails the card unless you switch off \"Fail on HTTP error\".", "Only http:// and https:// addresses work."]),
    };

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var method = ParamReader.GetString(context.Params, "method").Trim().ToUpperInvariant();
        if (method.Length == 0) method = "GET";
        if (!Methods.Contains(method)) throw new ArgumentException($"Unknown method '{method}'.");

        var urlText = _templates.Render(ParamReader.GetString(context.Params, "url"), ctx).Trim();
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"URL must be a full http(s) address: '{urlText}'.");

        var headers = new Dictionary<string, string>();
        var headersText = _templates.Render(ParamReader.GetString(context.Params, "headers"), ctx).Trim();
        if (headersText.Length > 0)
        {
            JToken parsed;
            try { parsed = JToken.Parse(headersText); }
            catch (JsonReaderException ex) { throw new ArgumentException($"Headers is not valid JSON: {ex.Message}"); }
            if (parsed is not JObject o) throw new ArgumentException("Headers must be a JSON object.");
            foreach (var p in o.Properties()) headers[p.Name] = p.Value.Type == JTokenType.String ? (string)p.Value! : p.Value.ToString(Formatting.None);
        }

        var bodyText = ParamReader.GetString(context.Params, "body");
        var body = bodyText.Length > 0 ? _templates.Render(bodyText, ctx) : null;

        var seconds = ParamReader.GetInt(context.Params, "timeoutSeconds", 30);
        if (seconds < 1 || seconds > 600) throw new ArgumentException("Timeout must be between 1 and 600 seconds.");

        var result = await _http.SendAsync(method, url, headers, body, TimeSpan.FromSeconds(seconds), ct);

        if (!result.Ok && ParamReader.GetBool(context.Params, "failOnError", true))
        {
            var detail = result.Text.Trim();
            if (detail.Length > 300) detail = detail[..300] + "…";
            throw new InvalidOperationException($"HTTP {result.Status}" + (detail.Length > 0 ? $": {detail}" : ""));
        }

        var output = new JObject
        {
            ["status"] = result.Status,
            ["ok"] = result.Ok,
            ["headers"] = JObject.FromObject(result.Headers),
            ["text"] = result.Text,
            ["truncated"] = result.Truncated,
        };
        var trimmed = result.Text.Trim();
        if (trimmed.Length > 0 && !result.Truncated && (trimmed[0] is '{' or '[' or '"' || char.IsDigit(trimmed[0]) || trimmed is "true" or "false" or "null"))
        {
            try { output["json"] = JToken.Parse(trimmed); } catch (JsonReaderException) { /* not JSON after all */ }
        }
        return output;
    }
}
