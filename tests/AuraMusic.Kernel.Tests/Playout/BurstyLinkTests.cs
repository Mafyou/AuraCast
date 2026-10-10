namespace AuraMusic.Kernel.Tests.Playout;

/// <summary>Bluetooth alone, minute after minute: what a listener hears, and how often it is cut or doctored.</summary>
public sealed class BurstyLinkTests
{
    const int StallEveryFrames = 500; // every ten seconds
    const string Untouched = "cuts 0, caught up 0, skipped 0";

    /// <summary>Packets arrive a handful at a time and, now and then, the radio stalls for a whole burst.</summary>
    static Func<int> Bursts(int smallest, int largest, int stallMs = 0)
    {
        var random = new Random(1);
        int sinceStall = 0;
        return () =>
        {
            int burst = random.Next(smallest, largest + 1);
            sinceStall += burst;
            if (stallMs == 0 || sinceStall < StallEveryFrames)
                return burst;
            sinceStall = 0;
            return stallMs / PlayoutTuning.FrameMs;
        };
    }

    static BurstyLinkSimulation Listener(Func<int> bursts, double clockErrorPpm = 0) =>
        new(PlayoutTuning.BluetoothMinLatencyMs, bursts) { ClockErrorPpm = clockErrorPpm };

    static string Events(BurstyLinkSimulation listener) =>
        $"cuts {listener.CutCount}, caught up {listener.CaughtUpCount}, skipped {listener.SkippedCount}";

    [Theory]
    [InlineData(4, 12, 0)]
    [InlineData(8, 14, 0)]
    [InlineData(4, 12, 100)]
    [InlineData(4, 12, -100)]
    public void SteadyBursts_ArePlayedUntouched(int smallest, int largest, double clockErrorPpm)
    {
        var listener = Listener(Bursts(smallest, largest), clockErrorPpm);

        listener.Run(TimeSpan.FromMinutes(10));

        Events(listener).ShouldBe(Untouched);
        listener.LatencyMs.ShouldBe(PlayoutTuning.BluetoothMinLatencyMs);
    }

    /// <summary>What two phones in a quiet flat were measured doing: a stall of 200 to 450 ms every few seconds.</summary>
    [Theory]
    [InlineData(300)]
    [InlineData(440)]
    [InlineData(520)]
    public void StallsTheBufferHolds_AreNotHeard(int stallMs)
    {
        var listener = Listener(Bursts(4, 12, stallMs));

        listener.Run(TimeSpan.FromMinutes(10));

        Events(listener).ShouldBe(Untouched);
    }

    /// <summary>
    /// A busier radio (a crowded room). Each stall used to be concealed, then caught up on by shortening packets
    /// for seconds, until the next one: a metallic sound that never stopped. One cut now teaches the latency
    /// what this link needs, and that is the last thing heard.
    /// </summary>
    [Theory]
    [InlineData(600)]
    [InlineData(700)]
    [InlineData(900)]
    public void LongerRecurringStalls_CutOnce_ThenAreNotHeard(int stallMs)
    {
        var listener = Listener(Bursts(4, 12, stallMs));

        listener.Run(TimeSpan.FromMinutes(10));

        Events(listener).ShouldBe("cuts 1, caught up 0, skipped 0");
        listener.LatencyMs.ShouldBeGreaterThanOrEqualTo(stallMs - PlayoutTuning.FrameMs);
    }

    [Fact]
    public void StallsLongerThanAnyBuffer_CutCleanly_WithoutShorteningOrSkipping()
    {
        var listener = Listener(Bursts(4, 12, stallMs: 1500));

        listener.Run(TimeSpan.FromMinutes(10));

        listener.CutCount.ShouldBeInRange(45, 62);     // one cut per stall, no more
        listener.ResyncedCount.ShouldBeGreaterThan(0); // and back in step right after it
        listener.CaughtUpCount.ShouldBe(0);
        listener.SkippedCount.ShouldBe(0);
        listener.LatencyMs.ShouldBe(PlayoutTuning.BluetoothMinLatencyMs + AdaptiveLatency.MaxExtraMs);
    }
}
