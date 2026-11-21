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

    public async Task<OllamaChatResult> ChatAsync(Connection c, OllamaChatRequest r, TimeSpan timeout, CancellationToken ct)
    {
        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(r.System)) messages.Add(new JObject { ["role"] = "system", ["content"] = r.System });
        messages.Add(new JObject { ["role"] = "user", ["content"] = r.Prompt });
        var body = new JObject { ["model"] = r.Model, ["messages"] = messages, ["stream"] = false };
        var options = new JObject();
        if (r.Temperature is { } t) options["temperature"] = t;
        if (r.MaxTokens is { } n) options["num_predict"] = n;
        if (options.HasValues) body["options"] = options;
        if (r.JsonMode) body["format"] = "json";
        if (r.Think is { } think) body["think"] = think;

        var json = await SendAsync(c, HttpMethod.Post, "/api/chat", body, timeout, ct);
        var message = json["message"] as JObject ?? throw new InvalidOperationException("Ollama answered without a message.");
        var thinking = (string?)message["thinking"];
        return new OllamaChatResult(
            (string?)message["content"] ?? "",
            string.IsNullOrEmpty(thinking) ? null : thinking,
            (string?)json["model"] ?? r.Model,
            (bool?)json["done"] ?? true,
            (string?)json["done_reason"],
            (int?)json["prompt_eval_count"],
            (int?)json["eval_count"],
            (double?)json["total_duration"] is { } ns ? Math.Round(ns / 1_000_000, 1) : null);
    }

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
            var detail = ErrorText(text);
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Ollama refused the request ({(int)response.StatusCode}). Check the API key of this connection.",
                HttpStatusCode.NotFound when detail.Contains("model", StringComparison.OrdinalIgnoreCase) => $"Ollama: {detail}",
                _ => $"Ollama answered {(int)response.StatusCode}" + (detail.Length > 0 ? $": {detail}" : "."),
            });
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
