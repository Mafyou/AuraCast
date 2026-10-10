namespace AuraMusic.Kernel.Tests.Playout;

/// <summary>Bluetooth alone, minute after minute: what a listener hears, and how often it is cut or doctored.</summary>
public sealed class BurstyLinkTests
{
    const int StallEveryFrames = 500; // every ten seconds

    /// <summary>Packets arrive a handful at a time and, now and then, the radio stalls for a whole burst.</summary>
    static Func<int> Bursts(int smallest, int largest, int stallFrames = 0)
    {
        var random = new Random(1);
        int sinceStall = 0;
        return () =>
        {
            int burst = random.Next(smallest, largest + 1);
            sinceStall += burst;
            if (stallFrames == 0 || sinceStall < StallEveryFrames)
                return burst;
            sinceStall = 0;
            return stallFrames;
        };
    }

    static BurstyLinkSimulation Listener(Func<int> bursts, double clockErrorPpm = 0) =>
        new(PlayoutTuning.BluetoothMinLatencyMs, bursts) { ClockErrorPpm = clockErrorPpm };

    static string Events(BurstyLinkSimulation listener) =>
        $"caught up {listener.CaughtUpCount}, concealed {listener.ConcealedCount}, skipped {listener.SkippedCount}, rebuffers {listener.RebufferCount}";

    [Theory]
    [InlineData(4, 12, 0)]
    [InlineData(8, 20, 0)]
    [InlineData(4, 12, 100)]
    [InlineData(4, 12, -100)]
    public void SteadyBursts_ArePlayedUntouched(int smallest, int largest, double clockErrorPpm)
    {
        var listener = Listener(Bursts(smallest, largest), clockErrorPpm);

        listener.Run(TimeSpan.FromMinutes(10));

        Events(listener).ShouldBe("caught up 0, concealed 0, skipped 0, rebuffers 0");
        listener.LatencyMs.ShouldBe(PlayoutTuning.BluetoothMinLatencyMs);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(360)]
    public void StallsTheBufferHolds_AreNotHeard(int stallMs)
    {
        var listener = Listener(Bursts(4, 12, stallMs / PlayoutTuning.FrameMs));

        listener.Run(TimeSpan.FromMinutes(10));

        Events(listener).ShouldBe("caught up 0, concealed 0, skipped 0, rebuffers 0");
    }

    /// <summary>
    /// The case that turned the sound metallic: each stall used to be concealed, then caught up on by shortening
    /// packets for seconds, until the next one. The latency now grows to what the link needs, and that is all.
    /// </summary>
    [Theory]
    [InlineData(440)]
    [InlineData(500)]
    [InlineData(600)]
    [InlineData(700)]
    public void RecurringStalls_AreLearnt_ThenNotHeard(int stallMs)
    {
        var listener = Listener(Bursts(4, 12, stallMs / PlayoutTuning.FrameMs));
        listener.Run(TimeSpan.FromMinutes(2));
        string learning = Events(listener);

        listener.Run(TimeSpan.FromMinutes(8));

        Events(listener).ShouldBe(learning);
        listener.CaughtUpCount.ShouldBe(0);
        listener.SkippedCount.ShouldBe(0);
        listener.LatencyMs.ShouldBeGreaterThan(PlayoutTuning.BluetoothMinLatencyMs);
    }

    [Fact]
    public void StallsLongerThanAnyBuffer_CutCleanly_WithoutShorteningOrSkipping()
    {
        var listener = Listener(Bursts(4, 12, stallFrames: 50)); // a full second, every ten seconds

        listener.Run(TimeSpan.FromMinutes(10));

        listener.RebufferCount.ShouldBeInRange(50, 62); // one cut per stall, no more
        listener.ResyncedCount.ShouldBeGreaterThan(0);  // and back in step right after it
        listener.CaughtUpCount.ShouldBe(0);
        listener.SkippedCount.ShouldBe(0);
        listener.LatencyMs.ShouldBe(PlayoutTuning.BluetoothMinLatencyMs + AdaptiveLatency.MaxExtraMs);
    }
}
