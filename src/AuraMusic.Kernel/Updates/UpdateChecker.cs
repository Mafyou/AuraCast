namespace AuraMusic.Kernel.Updates;

public sealed class UpdateChecker(IReleaseFeed feed)
{
    public async Task<UpdateCheck> CheckAsync(string installedVersion, CancellationToken stoppingToken)
    {
        try
        {
            var latest = await feed.GetLatestAsync(stoppingToken);
            // An unreadable installed version (a hand-made build) is never nagged about.
            return latest is not null && AppVersion.TryParse(installedVersion, out var installed) && latest.Version > installed
                ? new UpdateAvailable(latest)
                : new UpToDate();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return new CheckFailed(ex.Message); // offline, GitHub down, odd payload: try again next launch
        }
    }
}
