namespace AuraMusic.Kernel.Playout;

/// <summary>
/// Learns how much more audio this link needs in hand. When playback runs dry, the latency is raised by how
/// long it stayed dry, so the next stall of that size is ridden out silently instead of being concealed, then
/// caught up on; after a long calm it comes back down, slowly. A radio that stalls every few seconds settles
/// on what it takes; a clean link stays at the chosen latency.
/// </summary>
public sealed class AdaptiveLatency
{
    /// <summary>Added on top of the time playback stayed dry, so a stall of the same length just fits.</summary>
    public const int MarginMs = 40;

    /// <summary>The most that is ever added to the chosen latency.</summary>
    public const int MaxExtraMs = 400;

    /// <summary>Given back, one packet at a time, after this much playback without running dry (five minutes).</summary>
    public const int CalmFramesPerStepDown = 5 * 60 * 1000 / PlayoutTuning.FrameMs;

    /// <summary>A silence longer than this is the broadcast stopping or the link dropping, not a stall to learn from.</summary>
    public static readonly TimeSpan Outage = TimeSpan.FromSeconds(2);

    int calmFrames;

    /// <summary>Milliseconds to add to the chosen latency.</summary>
    public int ExtraMs { get; private set; }

    /// <summary>A packet was there in time and played.</summary>
    public void Played()
    {
        if (++calmFrames < CalmFramesPerStepDown)
            return;
        calmFrames = 0;
        ExtraMs = Math.Max(0, ExtraMs - PlayoutTuning.FrameMs);
    }

    /// <summary>Playback is back after having had nothing to play for <paramref name="dry"/>.</summary>
    public void Stalled(TimeSpan dry)
    {
        calmFrames = 0;
        if (dry > Outage)
            return;
        // In whole packets, as the jitter buffer counts.
        int missing = (int)Math.Ceiling((dry.TotalMilliseconds + MarginMs) / PlayoutTuning.FrameMs) * PlayoutTuning.FrameMs;
        ExtraMs = Math.Min(MaxExtraMs, ExtraMs + missing);
    }
}
