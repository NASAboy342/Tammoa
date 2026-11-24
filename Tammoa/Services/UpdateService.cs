using System.Net;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

public class UpdateService : IUpdateService
{
    public const string RepositoryMetadataKey = "RepositoryUrl";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly IAppInfoService _info;
    private readonly string? _repositoryUrl;

    /// <param name="repositoryUrl">https://github.com/owner/name (from the project file); empty = no update source.</param>
    public UpdateService(HttpClient http, IAppInfoService info, string? repositoryUrl)
    {
        _http = http;
        _info = info;
        _repositoryUrl = repositoryUrl;
    }

    /// <summary>The repository address written into the project file (AssemblyMetadata "RepositoryUrl").</summary>
    public static string? RepositoryUrlOf(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == RepositoryMetadataKey)?.Value;

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct)
    {
        var current = _info.GetVersion().Version;
        if (!TryGitHubRepo(_repositoryUrl, out var owner, out var repo))
            return new UpdateInfo(current, null, false, null, "No update source is set for this build.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            request.Headers.UserAgent.ParseAdd("Tammoa-update-check"); // GitHub refuses requests without one; it carries no personal data
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateInfo(current, null, false, null, "No release has been published yet.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return new UpdateInfo(current, null, false, null, "GitHub is limiting requests right now. Try again in a while.");
            if (!response.IsSuccessStatusCode)
                return new UpdateInfo(current, null, false, null, $"GitHub answered {(int)response.StatusCode}. Try again later.");

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var tag = ((string?)json["tag_name"])?.Trim() ?? "";
            var url = (string?)json["html_url"];
            if (url is null || !url.StartsWith("https://github.com/", StringComparison.Ordinal)) url = $"https://github.com/{owner}/{repo}/releases";
            if (!TryParseVersion(tag, out var latest))
                return new UpdateInfo(current, null, false, url, $"The latest release has an unreadable version ('{tag}').");
            var available = TryParseVersion(current, out var running) && latest > running;
            return new UpdateInfo(current, latest.ToString(3), available, url, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateInfo(current, null, false, null, "GitHub did not answer in time. Check your internet connection.");
        }
        catch (HttpRequestException)
        {
            return new UpdateInfo(current, null, false, null, "Could not reach GitHub. Check your internet connection.");
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return new UpdateInfo(current, null, false, null, "GitHub sent an answer that could not be read.");
        }
    }

    /// <summary>"v1.2.3", "1.2.3-beta+abc" -> 1.2.3 (numbers only; a pre-release counts as its base version).</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        var t = (text ?? "").Trim().TrimStart('v', 'V');
        var end = t.IndexOfAny(['-', '+', ' ']);
        if (end >= 0) t = t[..end];
        if (!Version.TryParse(t, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    public static bool TryGitHubRepo(string? url, out string owner, out string repo)
    {
        owner = repo = "";
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Host != "github.com") return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2) return false;
        owner = parts[0];
        repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return owner.Length > 0 && repo.Length > 0;
    }
}
