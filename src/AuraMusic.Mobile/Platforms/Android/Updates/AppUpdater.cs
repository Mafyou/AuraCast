namespace AuraMusic.Mobile.Updates;

/// <summary>Checks GitHub for a newer AuraMusic and installs it over the current one.</summary>
public static class AppUpdater
{
    const string ApkMimeType = "application/vnd.android.package-archive";

    static readonly HttpClient Http = new();
    static readonly UpdateChecker Checker = new(new GitHubReleaseFeed(Http, "Mafyou", "AuraMusic"));

    // Downloaded while AuraMusic was not yet allowed to install apps: installed when the user comes back.
    static string? pendingApk;

    public static Task<UpdateCheck> CheckAsync(CancellationToken stoppingToken)
    {
#if DEBUG
        // Local builds are signed with the debug key: a release could not be installed over them anyway.
        return Task.FromResult<UpdateCheck>(new UpToDate());
#else
        return Checker.CheckAsync(AppInfo.Current.VersionString, stoppingToken);
#endif
    }

    /// <summary>Android asks once per app before it may install others.</summary>
    public static bool CanInstall => Platform.AppContext.PackageManager!.CanRequestPackageInstalls();

    /// <returns>The path of the downloaded APK.</returns>
    public static async Task<string> DownloadAsync(AppRelease release, IProgress<double> progress, CancellationToken stoppingToken)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"AuraMusic-{release.Tag}.apk");
        using var response = await Http.GetAsync(release.ApkUrl, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
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
        return path;
    }

    /// <summary>
    /// Opens Android's "install unknown apps" switch for AuraMusic. Android forbids starting the installer
    /// from the background, so it is started by <see cref="ResumePendingInstall"/> once the user is back.
    /// </summary>
    public static void InstallWhenAllowed(string apk)
    {
        pendingApk = apk;
        var context = Platform.AppContext;
        var settings = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
            global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
        context.StartActivity(settings.AddFlags(ActivityFlags.NewTask));
    }

    /// <summary>Called when the app comes back to the foreground.</summary>
    public static void ResumePendingInstall()
    {
        if (pendingApk is not { } apk || !CanInstall)
            return;
        pendingApk = null;
        Install(apk);
    }

    /// <summary>Hands the APK to the package installer. Must run while the app is in the foreground.</summary>
    public static void Install(string apk)
    {
        // Same package, same signing key: Android installs it over the current version and keeps its data.
        var context = Platform.CurrentActivity ?? Platform.AppContext;
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, $"{context.PackageName}.fileProvider", new Java.IO.File(apk))!;
        var install = new Intent(Intent.ActionView)
            .SetDataAndType(uri, ApkMimeType)!
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(install);
    }
}
