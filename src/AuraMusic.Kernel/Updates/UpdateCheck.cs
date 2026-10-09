namespace AuraMusic.Kernel.Updates;

public sealed record UpToDate;
public sealed record UpdateAvailable(AppRelease Release);
public sealed record CheckFailed(string Reason);

public union UpdateCheck(UpToDate, UpdateAvailable, CheckFailed);
