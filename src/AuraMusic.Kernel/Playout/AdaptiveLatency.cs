namespace AuraMusic.Kernel.Playout;

/// <summary>
/// Learns how much more audio this link needs in hand. Each time playback runs dry the latency is raised a
/// step, so the next stall of that size is ridden out silently instead of being concealed, then caught up on;
/// after a long calm it comes back down, slowly. A radio that stalls every few seconds settles on what it
/// takes; a clean link stays at the chosen latency.
/// </summary>
public sealed class AdaptiveLatency
{
    /// <summary>Added when playback runs dry: about what a short concealment covers.</summary>
    public const int StepMs = 60;

    /// <summary>The most that is ever added to the chosen latency.</summary>
    public const int MaxExtraMs = 400;

    /// <summary>Given back, one packet at a time, after this much playback without running dry (five minutes).</summary>
    public const int CalmFramesPerStepDown = 5 * 60 * 1000 / PlayoutTuning.FrameMs;

    /// <summary>A silence longer than this is the broadcast stopping or the link dropping, not a stall to learn from.</summary>
    public static readonly TimeSpan Outage = TimeSpan.FromSeconds(2);

    int calmFrames;
    int addedThisRun;
    bool dry;

    /// <summary>Milliseconds to add to the chosen latency.</summary>
    public int ExtraMs { get; private set; }

    /// <summary>A packet was there in time and played.</summary>
    public void Played()
    {
        dry = false;
        addedThisRun = 0;
        if (++calmFrames < CalmFramesPerStepDown)
            return;
        calmFrames = 0;
        ExtraMs = Math.Max(0, ExtraMs - PlayoutTuning.FrameMs);
    }

    /// <summary>Nothing to play, a packet is being concealed. Counts once per run of missing packets.</summary>
    public void RanDry()
    {
        calmFrames = 0;
        if (dry)
            return;
        dry = true;
        Raise();
    }

    /// <summary>Playback had to stop and wait <paramref name="waited"/> for the buffer to fill again.</summary>
    public void Rebuffered(TimeSpan waited)
    {
        calmFrames = 0;
        if (waited > Outage)
        {
            // Not jitter: take back what this run added.
            ExtraMs -= addedThisRun;
            addedThisRun = 0;
            return;
        }
        Raise(); // concealing was not enough: this stall was a long one
    }

    void Raise()
    {
        int step = Math.Min(StepMs, MaxExtraMs - ExtraMs);
        ExtraMs += step;
        addedThisRun += step;
    }
}
