namespace AuraMusic.Kernel.Playout;

/// <summary>
/// Decides, every 20 ms on the playback clock, what to do with the next packet of the jitter buffer.
/// </summary>
public sealed class PlayoutController(IPlayoutMetrics metrics)
{
    /// <summary>Packets to have in hand before (re)starting playback.</summary>
    public int PrebufferFrames
    {
        get;
        init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(PrebufferFrames), value, "Must be positive.");
    } = 6;

    /// <summary>
    /// Above this backlog we are lagging a bit: packets are played slightly shorter until we are back on time.
    /// Keep it a little above <see cref="PrebufferFrames"/> so normal jitter does not trigger it.
    /// </summary>
    public int CatchUpAboveFrames
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(CatchUpAboveFrames), value, "Cannot be negative.");
    } = 8;

    /// <summary>Last resort, far behind (the link stalled then burst): a packet is dropped outright.</summary>
    public int MaxBacklogFrames
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxBacklogFrames), value, "Cannot be negative.");
    } = 20;

    /// <summary>A hiccup is concealed; more consecutive missing packets than this means the link stalled.</summary>
    public int MaxConcealedFrames
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxConcealedFrames), value, "Cannot be negative.");
    } = 10;

    int concealedInARow;

    /// <param name="packet">The next packet, or <see langword="null"/> when it did not arrive in time.</param>
    /// <param name="backlog">Packets still waiting in the jitter buffer after this one.</param>
    public PlayoutStep Next(byte[]? packet, int backlog)
    {
        if (packet is null)
        {
            if (++concealedInARow > MaxConcealedFrames)
            {
                concealedInARow = 0;
                metrics.Rebuffered();
                return new Rebuffer();
            }
            metrics.Concealed();
            return new Conceal();
        }

        concealedInARow = 0;
        if (backlog > MaxBacklogFrames)
        {
            metrics.Skipped();
            return new Skip(packet);
        }
        if (backlog > CatchUpAboveFrames)
        {
            metrics.CaughtUp();
            return new CatchUp(packet);
        }
        return new Play(packet);
    }
}
