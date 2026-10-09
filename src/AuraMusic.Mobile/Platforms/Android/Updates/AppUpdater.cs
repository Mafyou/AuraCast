namespace AuraMusic.Mobile.Updates;

/// <summary>Checks GitHub for a newer AuraMusic and installs it over the current one.</summary>
public static class AppUpdater
{
    const string ApkMimeType = "application/vnd.android.package-archive";
    static readonly TimeSpan PermissionTimeout = TimeSpan.FromMinutes(2);

    static readonly HttpClient Http = new();
    static readonly UpdateChecker Checker = new(new GitHubReleaseFeed(Http, "Mafyou", "AuraMusic"));

    public static Task<UpdateCheck> CheckAsync(CancellationToken stoppingToken)
    {
#if DEBUG
        // Local builds are signed with the debug key: a release could not be installed over them anyway.
        return Task.FromResult<UpdateCheck>(new UpToDate());
#else
        return Checker.CheckAsync(AppInfo.Current.VersionString, stoppingToken);
#endif
    }

    /// <returns><see langword="false"/> when the user did not allow AuraMusic to install apps.</returns>
    public static async Task<bool> DownloadAndInstallAsync(AppRelease release, IProgress<double> progress, CancellationToken stoppingToken)
    {
        var apk = Path.Combine(FileSystem.CacheDirectory, $"AuraMusic-{release.Tag}.apk");
        await DownloadAsync(release.ApkUrl, apk, progress, stoppingToken);
        if (!await EnsureInstallPermissionAsync(stoppingToken))
            return false;

        // Same package, same signing key: Android installs it over the current version and keeps its data.
        await Launcher.Default.OpenAsync(new OpenFileRequest("AuraMusic", new ReadOnlyFile(apk, ApkMimeType)));
        return true;
    }

    static async Task DownloadAsync(Uri url, string path, IProgress<double> progress, CancellationToken stoppingToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(stoppingToken);
        await using var target = File.Create(path);
        var buffer = new byte[81_920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, stoppingToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), stoppingToken);
            done += read;
            if (total > 0)
                progress.Report((double)done / total.Value);
        }
    }

    /// <summary>Android asks once per app: open its "install unknown apps" switch and wait for the user to come back.</summary>
    static async Task<bool> EnsureInstallPermissionAsync(CancellationToken stoppingToken)
    {
        var context = Platform.AppContext;
        var packages = context.PackageManager!;
        if (packages.CanRequestPackageInstalls())
            return true;

        var settings = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
            global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
        context.StartActivity(settings.AddFlags(ActivityFlags.NewTask));

        var deadline = DateTime.UtcNow + PermissionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, stoppingToken);
            if (packages.CanRequestPackageInstalls())
                return true;
        }
        return false;
    }
}
