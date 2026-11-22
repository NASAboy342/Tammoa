using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

public class OllamaClient : IOllamaClient
{
    private readonly HttpClient _http;

    public OllamaClient(HttpClient http) => _http = http;

    public async Task<string> GetVersionAsync(Connection c, CancellationToken ct)
    {
        var json = await SendAsync(c, HttpMethod.Get, "/api/version", null, TimeSpan.FromSeconds(10), ct);
        return (string?)json["version"] ?? throw new InvalidOperationException($"{c.BaseUrl} answered, but it does not look like an Ollama server.");
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(Connection c, CancellationToken ct)
    {
        var json = await SendAsync(c, HttpMethod.Get, "/api/tags", null, TimeSpan.FromSeconds(15), ct);
        return (json["models"] as JArray ?? new JArray()).Select(m => (string?)m["name"]).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<OllamaChatResult> ChatAsync(Connection c, OllamaChatRequest r, TimeSpan timeout, CancellationToken ct, Action<string, string>? onDelta = null)
    {
        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(r.System)) messages.Add(new JObject { ["role"] = "system", ["content"] = r.System });
        messages.Add(new JObject { ["role"] = "user", ["content"] = r.Prompt });
        var body = BuildBody(r.Model, messages, null, r.Temperature, r.MaxTokens, r.Think);
        if (r.JsonMode) body["format"] = "json";
        return (await StreamChatAsync(c, body, r.Model, timeout, ct, onDelta)).Chat;
    }

    public Task<OllamaTurnResult> ChatTurnAsync(Connection c, OllamaTurnRequest r, TimeSpan timeout, CancellationToken ct, Action<string, string>? onDelta = null) =>
        StreamChatAsync(c, BuildBody(r.Model, r.Messages, r.Tools, r.Temperature, r.MaxTokens, r.Think), r.Model, timeout, ct, onDelta);

    private static JObject BuildBody(string model, JArray messages, JArray? tools, double? temperature, int? maxTokens, bool? think)
    {
        var body = new JObject { ["model"] = model, ["messages"] = messages, ["stream"] = true };
        if (tools is { Count: > 0 }) body["tools"] = tools;
        var options = new JObject();
        if (temperature is { } t) options["temperature"] = t;
        if (maxTokens is { } n) options["num_predict"] = n;
        if (options.HasValues) body["options"] = options;
        if (think is { } th) body["think"] = th;
        return body;
    }

    /// <summary>Reads Ollama's newline-delimited JSON stream, handing the growing text to <paramref name="onDelta"/>.</summary>
    private async Task<OllamaTurnResult> StreamChatAsync(Connection c, JObject body, string requestedModel, TimeSpan timeout, CancellationToken ct, Action<string, string>? onDelta)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, c.BaseUrl.TrimEnd('/') + "/api/chat");
        if (!string.IsNullOrEmpty(c.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.ApiKey);
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        using var silence = new CancellationTokenSource(timeout); // restarted by every piece the model sends
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
        var text = new StringBuilder();
        var thinking = new StringBuilder();
        JObject? last = null;
        var toolCalls = new List<(string Name, JObject Arguments)>();
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(linked.Token);
                throw new InvalidOperationException(ErrorMessage(c, response.StatusCode, ErrorText(errorBody)));
            }
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(linked.Token) is { } line)
            {
                silence.CancelAfter(timeout);
                if (line.Trim().Length == 0) continue;
                JObject piece;
                try { piece = JObject.Parse(line); }
                catch (Newtonsoft.Json.JsonReaderException) { throw new InvalidOperationException($"{c.BaseUrl} answered, but it does not look like an Ollama server."); }
                if ((string?)piece["error"] is { Length: > 0 } error) throw new InvalidOperationException($"Ollama: {error}");
                if (piece["message"] is JObject m)
                {
                    text.Append((string?)m["content"]);
                    thinking.Append((string?)m["thinking"]);
                    if (m["tool_calls"] is JArray calls) foreach (var call in calls) toolCalls.Add(ReadToolCall(call));
                    onDelta?.Invoke(thinking.ToString(), text.ToString());
                }
                if ((bool?)piece["done"] == true) last = piece;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Ollama at {c.BaseUrl} was silent for {timeout.TotalSeconds:0.##} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach Ollama at {c.BaseUrl}: {ex.Message}");
        }

        var chat = new OllamaChatResult(
            text.ToString(),
            thinking.Length == 0 ? null : thinking.ToString(),
            (string?)last?["model"] ?? requestedModel,
            last is not null,
            (string?)last?["done_reason"],
            (int?)last?["prompt_eval_count"],
            (int?)last?["eval_count"],
            (double?)last?["total_duration"] is { } ns ? Math.Round(ns / 1_000_000, 1) : null);
        return new OllamaTurnResult(chat, toolCalls);
    }

    /// <summary>{ function: { name, arguments } }; the arguments are an object, but some servers send them as JSON text.</summary>
    private static (string Name, JObject Arguments) ReadToolCall(JToken call)
    {
        var fn = call["function"] ?? call;
        var name = (string?)fn["name"] ?? "";
        var args = fn["arguments"];
        if (args is JValue { Type: JTokenType.String } text)
        {
            try { args = JToken.Parse((string)text!); } catch (Newtonsoft.Json.JsonReaderException) { args = null; }
        }
        return (name, args as JObject ?? new JObject());
    }

    private static string ErrorMessage(Connection c, HttpStatusCode status, string detail) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Ollama refused the request ({(int)status}). Check the API key of this connection.",
        _ when detail.Contains("does not support tools", StringComparison.OrdinalIgnoreCase) =>
            "This model does not support tools. Choose another model (for example qwen3 or llama3.1) or untick the tools.",
        HttpStatusCode.NotFound when detail.Contains("model", StringComparison.OrdinalIgnoreCase) => $"Ollama: {detail}",
        _ => $"Ollama answered {(int)status}" + (detail.Length > 0 ? $": {detail}" : "."),
    };

    private async Task<JObject> SendAsync(Connection c, HttpMethod method, string path, JObject? body, TimeSpan timeout, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, c.BaseUrl.TrimEnd('/') + path);
        if (!string.IsNullOrEmpty(c.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.ApiKey);
        if (body is not null) request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            using var response = await _http.SendAsync(request, linked.Token);
            var text = await response.Content.ReadAsStringAsync(linked.Token);
            if (response.IsSuccessStatusCode)
            {
                try { return JObject.Parse(text); }
                catch (Newtonsoft.Json.JsonReaderException) { throw new InvalidOperationException($"{c.BaseUrl} answered, but it does not look like an Ollama server."); }
            }
            throw new InvalidOperationException(ErrorMessage(c, response.StatusCode, ErrorText(text)));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Ollama at {c.BaseUrl} did not answer within {timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach Ollama at {c.BaseUrl}: {ex.Message}");
        }
    }

    private static string ErrorText(string body)
    {
        try { return (string?)JObject.Parse(body)["error"] ?? ""; }
        catch (Newtonsoft.Json.JsonReaderException) { return body.Trim().Length > 200 ? body.Trim()[..200] : body.Trim(); }
    }
}
