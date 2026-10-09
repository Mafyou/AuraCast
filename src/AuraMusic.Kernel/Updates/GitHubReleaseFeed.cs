namespace AuraMusic.Kernel.Updates;

/// <summary>Reads the latest release of a public GitHub repository (no token needed).</summary>
public sealed class GitHubReleaseFeed(HttpClient http, string owner, string repository) : IReleaseFeed
{
    public async Task<AppRelease?> GetLatestAsync(CancellationToken stoppingToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("AuraMusic"); // GitHub rejects requests without one
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, stoppingToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null; // no release published yet
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(stoppingToken));
    }

    public static AppRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        if (!AppVersion.TryParse(tag, out var version) || !root.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name is not null && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url))
                return new AppRelease(version, tag!, url);
        }
        return null;
    }
}
