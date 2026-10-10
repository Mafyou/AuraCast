namespace AuraMusic.Kernel.Playout;

/// <summary>
/// Decides, every 20 ms on the playback clock, what to do with the next packet of the jitter buffer.
/// </summary>
public sealed class PlayoutController(IPlayoutMetrics metrics)
{
    /// <summary>Packets to have in hand before (re)starting playback: enough to ride out a Bluetooth stall.</summary>
    public int PrebufferFrames
    {
        get;
        set => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(PrebufferFrames), value, "Must be positive.");
    } = 10;

    /// <summary>
    /// Above this backlog we are lagging: some packets are played slightly shorter until we are back on time.
    /// Compared with the backlog's trend, not its value of the moment: a link that delivers in bursts
    /// (Bluetooth) peaks well above its average at every burst, and shortening on those peaks is heard.
    /// </summary>
    public int CatchUpAboveFrames
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(CatchUpAboveFrames), value, "Cannot be negative.");
    } = 16;

    /// <summary>
    /// Shorten at most one packet in this many: splicing every packet is heard as a sped-up, metallic sound,
    /// one in five (1 ms per 100 ms) is not.
    /// </summary>
    public int CatchUpEveryFrames
    {
        get;
        set => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(CatchUpEveryFrames), value, "Must be positive.");
    } = 5;

    /// <summary>Last resort, far behind (the link stalled then burst): a packet is dropped outright.</summary>
    public int MaxBacklogFrames
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxBacklogFrames), value, "Cannot be negative.");
    } = 30;

    /// <summary>
    /// Concealment past a few tens of milliseconds sounds metallic: beyond this many missing packets in a row,
    /// go silent and rebuild the cushion instead.
    /// </summary>
    public int MaxConcealedFrames
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxConcealedFrames), value, "Cannot be negative.");
    } = 4;

    /// <summary>
    /// Retargets the jitter buffer while playing (the sync slider): how many packets to keep in hand, with the
    /// catch-up and last-resort thresholds following at their usual distance above.
    /// </summary>
    public void SetCushion(int prebufferFrames)
    {
        PrebufferFrames = prebufferFrames;
        CatchUpAboveFrames = prebufferFrames + PlayoutTuning.CatchUpMarginFrames;
        MaxBacklogFrames = prebufferFrames + PlayoutTuning.SkipMarginFrames;
    }

    // The backlog jumps by a whole burst when one arrives: follow its trend (about a second), not that.
    const double BacklogSmoothing = 0.02;

    /// <summary>The buffer was just brought back to its cushion by hand (after a stall): judge the backlog afresh.</summary>
    public void Resynced() => trendPrimed = false;

    int concealedInARow;
    int sinceCatchUp = int.MaxValue / 2;
    double backlogTrend;
    bool trendPrimed;

    /// <param name="packet">The next packet, or <see langword="null"/> when it did not arrive in time.</param>
    /// <param name="backlog">Packets still waiting in the jitter buffer after this one.</param>
    public PlayoutStep Next(byte[]? packet, int backlog)
    {
        if (packet is null)
        {
            if (++concealedInARow > MaxConcealedFrames)
            {
                concealedInARow = 0;
                trendPrimed = false; // playback restarts from a fresh buffer
                metrics.Rebuffered();
                return new Rebuffer();
            }
            metrics.Concealed();
            return new Conceal();
        }

        concealedInARow = 0;
        sinceCatchUp++;
        backlogTrend = trendPrimed ? backlogTrend + (backlog - backlogTrend) * BacklogSmoothing : backlog;
        trendPrimed = true;
        if (backlog > MaxBacklogFrames)
        {
            metrics.Skipped();
            return new Skip(packet);
        }
        if (backlogTrend > CatchUpAboveFrames && sinceCatchUp >= CatchUpEveryFrames)
        {
            sinceCatchUp = 0;
            metrics.CaughtUp();
            return new CatchUp(packet);
        }
        return new Play(packet);
    }
}
