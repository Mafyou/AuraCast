#if !PLAY_STORE
// Here rather than in the manifest: the Play Store refuses an app that asks to install packages.
[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]
#endif

namespace AuraMusic.Mobile.Updates;

/// <summary>Checks GitHub for a newer AuraMusic and installs it over the current one.</summary>
public static class AppUpdater
{
    /// <summary>False on a Play Store build: the store updates the app, which may not update itself.</summary>
    public static bool SelfUpdates =>
#if PLAY_STORE
        false;
#else
        true;
#endif

    const string ApkMimeType = "application/vnd.android.package-archive";

    // No global timeout: the 40 MB APK can take longer than HttpClient's default 100 s on a slow network.
    // The check has its own short one.
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    static readonly UpdateChecker Checker = new(new GitHubReleaseFeed(Http, "Mafyou", "AuraMusic"));

    // Downloaded while AuraMusic was not yet allowed to install apps: installed when the user comes back.
    static string? pendingApk;

    public static Task<UpdateCheck> CheckAsync(CancellationToken stoppingToken)
    {
#if DEBUG || PLAY_STORE
        // Local builds are signed with the debug key: a release could not be installed over them anyway.
        // Play Store builds are updated by the store.
        return Task.FromResult<UpdateCheck>(new UpToDate());
#else
        return CheckWithTimeoutAsync(stoppingToken);
#endif
    }

    static async Task<UpdateCheck> CheckWithTimeoutAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(CheckTimeout);
        return await Checker.CheckAsync(AppInfo.Current.VersionString, timeout.Token);
    }

    /// <summary>Android asks once per app before it may install others.</summary>
    public static bool CanInstall => Platform.AppContext.PackageManager!.CanRequestPackageInstalls();

    /// <returns>The path of the downloaded APK.</returns>
    public static async Task<string> DownloadAsync(AppRelease release, IProgress<double> progress, CancellationToken stoppingToken)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"AuraMusic-{release.Tag}.apk");
        // Earlier downloads are 40 MB each and of no use once installed.
        foreach (var old in Directory.EnumerateFiles(FileSystem.CacheDirectory, "AuraMusic-*.apk"))
            if (old != path)
                File.Delete(old);
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
