namespace AuraMusic.Kernel.Multipath;

/// <summary>
/// Merges the same numbered stream arriving over several links (Bluetooth and Wi-Fi): the first copy of
/// each packet goes through, later copies and anything older than what was already played are dropped.
/// Thread-safe: each link reads on its own thread.
/// </summary>
public sealed class SequenceGate
{
    readonly Lock gate = new();
    uint? session;
    uint last;
    bool anyAccepted;

    /// <summary>The broadcast being followed; packets from any other one (another master) are refused.</summary>
    public uint? Session
    {
        get
        {
            lock (gate)
                return session;
        }
    }

    /// <summary>A link has read the header of <paramref name="broadcast"/>.</summary>
    /// <returns><see langword="false"/> when another broadcast is already being followed: that link should give up.</returns>
    public bool Join(uint broadcast)
    {
        lock (gate)
        {
            if (session is null)
            {
                session = broadcast;
                anyAccepted = false;
            }
            return session == broadcast;
        }
    }

    /// <returns><see langword="true"/> for the first copy of a packet newer than everything let through so far.</returns>
    public bool TryAccept(uint broadcast, uint sequence)
    {
        lock (gate)
        {
            if (session != broadcast)
                return false;
            // Wrap-safe "newer than": sequence numbers roll over after ~2.7 years of 20 ms packets anyway.
            if (anyAccepted && (int)(sequence - last) <= 0)
                return false;
            last = sequence;
            anyAccepted = true;
            return true;
        }
    }

    /// <summary>Every link is down: the next one to connect may follow any broadcast.</summary>
    public void Reset()
    {
        lock (gate)
        {
            session = null;
            anyAccepted = false;
        }
    }
}
