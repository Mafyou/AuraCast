namespace AuraMusic.Kernel.Playout;

/// <summary>
/// The listener's one setting: how much audio it keeps in hand. Less is closer to the master's own sound
/// (useful in the same room), more rides out longer radio hiccups. Read by the playback thread while playing.
/// </summary>
public static class PlayoutTuning
{
    public const int MinLatencyMs = 80;
    public const int MaxLatencyMs = 500;
    public const int DefaultLatencyMs = 200;
    public const int FrameMs = 20;

    /// <summary>Of the total latency, the part that sits in the audio output rather than in the jitter buffer.</summary>
    public const int OutputShareMs = 60;

    /// <summary>Backlog above the cushion at which playback catches up by shortening packets.</summary>
    public const int CatchUpMarginFrames = 6;

    /// <summary>Backlog above the cushion at which a packet is dropped outright.</summary>
    public const int SkipMarginFrames = 20;

    static volatile int targetLatencyMs = DefaultLatencyMs;

    /// <summary>Total audio kept between reception and the speaker, in milliseconds.</summary>
    public static int TargetLatencyMs
    {
        get => targetLatencyMs;
        set => targetLatencyMs = Math.Clamp(value, MinLatencyMs, MaxLatencyMs);
    }

    /// <summary>Packets to hold in the jitter buffer for a given total latency.</summary>
    public static int CushionFrames(int latencyMs) => Math.Max(1, (latencyMs - OutputShareMs) / FrameMs);
}
