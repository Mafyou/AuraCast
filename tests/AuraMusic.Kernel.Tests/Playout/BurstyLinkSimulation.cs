namespace AuraMusic.Kernel.Tests.Playout;

/// <summary>
/// A listener on a link that delivers in bursts and sometimes stalls (Bluetooth alone, a crowded room), driven
/// by the real playout classes the way the playback thread drives them: one packet per turn, the audio output
/// kept full, the level measured after each packet.
/// </summary>
sealed class BurstyLinkSimulation : IPlayoutMetrics
{
    const double FrameMs = PlayoutTuning.FrameMs;
    const double CatchUpShaveMs = 1;
    // A late packet is waited for while the output still holds this much.
    const double GraceMs = PlayoutTuning.OutputShareMs - 20;
    // The master keeps this many packets for a link that does not take them; older ones are lost.
    const int MasterQueueFrames = 50;

    readonly PlayoutController playout;
    readonly DriftController drift = new(48_000);
    readonly AdaptiveLatency adaptive = new();
    readonly int chosenLatencyMs;
    readonly Func<int> nextBurst;
    readonly List<double> arrivals = []; // arrivals[k]: when the k-th packet that gets through reaches the listener
    int produced, consumed, latencyMs;
    double now;

    public BurstyLinkSimulation(int latencyMs, Func<int> nextBurst)
    {
        this.nextBurst = nextBurst;
        chosenLatencyMs = latencyMs;
        playout = new PlayoutController(this);
        Retarget();
    }

    public int CaughtUpCount { get; private set; }

    public int ConcealedCount { get; private set; }

    public int SkippedCount { get; private set; }

    public int RebufferCount { get; private set; }

    /// <summary>Packets thrown away to get back in step after a rebuffer.</summary>
    public int ResyncedCount { get; private set; }

    public double Correction => drift.Correction;

    public int LatencyMs => latencyMs;

    /// <summary>The listener's clock runs this much faster than the master's.</summary>
    public double ClockErrorPpm { get; init; }

    public void CaughtUp() => CaughtUpCount++;

    public void Concealed() => ConcealedCount++;

    public void Skipped() => SkippedCount++;

    public void Rebuffered() => RebufferCount++;

    void Retarget()
    {
        int wanted = chosenLatencyMs + adaptive.ExtraMs;
        if (wanted == latencyMs)
            return;
        latencyMs = wanted;
        playout.SetCushion(PlayoutTuning.CushionFrames(latencyMs));
        drift.TargetMs = PlayoutTuning.ReachableLatencyMs(latencyMs);
    }

    /// <summary>A packet is produced every 20 ms; the link hands them over a whole burst at a time.</summary>
    void DeliverUntil(int packet)
    {
        while (arrivals.Count <= packet)
        {
            int burst = nextBurst();
            produced += burst;
            for (int i = 0; i < Math.Min(burst, MasterQueueFrames); i++)
                arrivals.Add(produced * FrameMs);
        }
    }

    int Delivered()
    {
        int count = consumed;
        for (DeliverUntil(count); arrivals[count] <= now; DeliverUntil(count))
            count++;
        return count;
    }

    void WaitFor(int packets)
    {
        DeliverUntil(consumed + packets - 1);
        now = Math.Max(now, arrivals[consumed + packets - 1]);
    }

    public void Run(TimeSpan duration)
    {
        if (consumed == 0)
            WaitFor(playout.PrebufferFrames);
        double end = now + duration.TotalMilliseconds;
        while (now < end)
        {
            Retarget();
            int available = Delivered() - consumed;
            if (available == 0)
            {
                DeliverUntil(consumed);
                if (arrivals[consumed] - now <= GraceMs)
                {
                    now = arrivals[consumed];
                    continue;
                }
                if (playout.Next(null, 0) is Rebuffer)
                {
                    double waitStart = now;
                    WaitFor(playout.PrebufferFrames);
                    adaptive.Rebuffered(TimeSpan.FromMilliseconds(now - waitStart));
                    int excess = Delivered() - consumed - playout.PrebufferFrames;
                    if (excess > 0)
                    {
                        consumed += excess;
                        ResyncedCount += excess;
                    }
                    drift.Restart();
                    continue;
                }
                drift.RanDry();
                adaptive.RanDry();
                Play(FrameMs);
                continue;
            }

            consumed++;
            switch (playout.Next([0], available - 1))
            {
                case Skip:
                    continue;
                case CatchUp:
                    adaptive.Played();
                    Play(FrameMs - CatchUpShaveMs);
                    break;
                default:
                    adaptive.Played();
                    Play(FrameMs);
                    break;
            }
        }
    }

    void Play(double audioMs)
    {
        now += audioMs / ((1 + drift.Correction) * (1 + ClockErrorPpm / 1e6));
        drift.Update((Delivered() - consumed) * FrameMs + PlayoutTuning.OutputShareMs);
    }
}
