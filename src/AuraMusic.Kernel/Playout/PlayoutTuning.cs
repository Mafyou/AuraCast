namespace AuraMusic.Kernel.Playout;

/// <summary>
/// The listener's one setting: how much audio it keeps in hand. Less is closer to the master's own sound
/// (useful in the same room), more rides out longer radio hiccups. Read by the playback thread while playing.
/// </summary>
public static class PlayoutTuning
{
    public const int MinLatencyMs = OutputShareMs + MinCushionFrames * FrameMs;
    public const int MaxLatencyMs = 500;
    public const int DefaultLatencyMs = 200;
    public const int FrameMs = 20;

    /// <summary>Of the total latency, the part that sits in the audio output rather than in the jitter buffer.</summary>
    public const int OutputShareMs = 60;

    /// <summary>
    /// The jitter buffer never aims below this: with a single packet in hand, the slightest radio delay runs
    /// it dry and the sound stops every few seconds.
    /// </summary>
    public const int MinCushionFrames = 2;

    /// <summary>
    /// Backlog above the cushion at which playback catches up by shortening packets. Well above: a smaller
    /// excess is played off by the drift correction, which cannot be heard.
    /// </summary>
    public const int CatchUpMarginFrames = 15;

    /// <summary>Backlog above the cushion at which a packet is dropped outright.</summary>
    public const int SkipMarginFrames = 30;

    static volatile int targetLatencyMs = DefaultLatencyMs;

    /// <summary>Total audio kept between reception and the speaker, in milliseconds.</summary>
    public static int TargetLatencyMs
    {
        get => targetLatencyMs;
        set => targetLatencyMs = Math.Clamp(value, MinLatencyMs, MaxLatencyMs);
    }

    /// <summary>
    /// Bluetooth alone delivers in bursts and, measured between two phones in a quiet flat, stalls for 200 to
    /// 450 ms every few seconds: it needs this much in hand whatever the setting. <see cref="AdaptiveLatency"/>
    /// adds to it where the radio is busier.
    /// </summary>
    public const int BluetoothMinLatencyMs = 500;

    /// <summary>The setting, raised to what the links in use can sustain.</summary>
    public static int LatencyFor(Links links) =>
        links == Links.Bluetooth ? Math.Max(TargetLatencyMs, BluetoothMinLatencyMs) : TargetLatencyMs;

    /// <summary>Packets to hold in the jitter buffer for a given total latency.</summary>
    /// <param name="outputMs">What the audio output really holds: a phone may refuse a buffer as small as asked.</param>
    public static int CushionFrames(int latencyMs, int outputMs = OutputShareMs) =>
        Math.Max(MinCushionFrames, (latencyMs - outputMs) / FrameMs);

    /// <summary>
    /// The latency actually aimed for: the wish, raised to what this phone's output and the smallest cushion
    /// allow. Aiming lower would have the drift controller speed up for ever, emptying the buffer again and again.
    /// </summary>
    public static int ReachableLatencyMs(int latencyMs, int outputMs = OutputShareMs) =>
        outputMs + CushionFrames(latencyMs, outputMs) * FrameMs;
}
