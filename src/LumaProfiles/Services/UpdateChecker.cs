using System.Net.Http;
using System.Text.Json;

namespace LumaProfiles.Services;

/// <summary>A published release newer than (or equal to) the running one.</summary>
public sealed record UpdateInfo(Version Version, string Url);

public interface IUpdateChecker
{
    /// <summary>The latest published release, or null when it cannot be read (offline, rate limit...).</summary>
    Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads the latest release from the GitHub API. Only the version and the page link are used.</summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    public const string ReleasesPage = "https://github.com/CodigoLimpioCo/luma-profiles/releases/latest";
    private const string LatestReleaseApi = "https://api.github.com/repos/CodigoLimpioCo/luma-profiles/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(LatestReleaseApi, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Parse(json.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Warn("Update check failed.", exception);
            return null;
        }
    }

    internal static UpdateInfo? Parse(JsonElement release)
    {
        if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        if (!release.TryGetProperty("tag_name", out var tag) || !TryParseVersion(tag.GetString(), out var version)) return null;
        var url = release.TryGetProperty("html_url", out var link) && link.GetString() is { Length: > 0 } page ? page : ReleasesPage;
        return new UpdateInfo(version, url);
    }

    /// <summary>Accepts "v0.10.0", "0.10" or "0.10.0-beta" (the suffix is ignored).</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaProfiles-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
