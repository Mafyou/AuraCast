namespace AuraMusic.Kernel.Updates;

public interface IReleaseFeed
{
    /// <returns>The latest release that ships an APK, or <see langword="null"/> when there is none.</returns>
    Task<AppRelease?> GetLatestAsync(CancellationToken stoppingToken);
}
