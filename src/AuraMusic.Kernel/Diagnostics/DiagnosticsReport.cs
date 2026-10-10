namespace AuraMusic.Kernel.Diagnostics;

/// <summary>One of the master's links to a listener, over the last second.</summary>
public readonly record struct LinkReport(string Listener, Links Kind, double FramesPerSecond, int Queued, bool Carrying);

/// <summary>What the master's phone is doing, about once a second.</summary>
public sealed record MasterReport(bool NativeCodec, int BitrateKbps, double EncodeMs, int EncodeBacklog, ImmutableArray<LinkReport> Links);

/// <summary>What a listener's phone is doing, about once a second.</summary>
public sealed record ListenerReport(
    bool NativeCodec,
    Links Links,
    double BluetoothFramesPerSecond,
    double WifiFramesPerSecond,
    int Kbps,
    int BufferedMs,
    int TargetMs,
    double DriftPpm,
    int CaughtUp,
    int Concealed,
    int Skipped,
    int Rebuffers,
    int Dropped);

/// <summary>Neither sharing nor listening.</summary>
public sealed record NoReport;

public union DiagnosticsReport(NoReport, MasterReport, ListenerReport);

/// <summary>Latest figures from whichever service is running, for the diagnostics screen.</summary>
public static class DiagnosticsHub
{
    public static event Action<DiagnosticsReport>? Updated;

    public static DiagnosticsReport Current
    {
        get;
        private set
        {
            field = value;
            Updated?.Invoke(value);
        }
    } = new NoReport();

    public static void Publish(DiagnosticsReport report) => Current = report;
}
