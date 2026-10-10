namespace AuraMusic.Kernel.Tests.Playout;

public sealed class AdaptiveLatencyTests
{
    static void Play(AdaptiveLatency latency, int frames)
    {
        for (int i = 0; i < frames; i++)
            latency.Played();
    }

    static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void CleanLink_AddsNothing()
    {
        var latency = new AdaptiveLatency();

        Play(latency, 3 * AdaptiveLatency.CalmFramesPerStepDown);

        latency.ExtraMs.ShouldBe(0);
    }

    [Theory]
    [InlineData(1, 60)]    // even the shortest dry spell: its margin, in whole packets
    [InlineData(100, 140)]
    [InlineData(130, 180)] // 170 ms rounded up to whole packets
    [InlineData(300, 340)]
    public void Stall_RaisesTheLatencyByHowLongPlaybackStayedDry(int dryMs, int expectedExtraMs)
    {
        var latency = new AdaptiveLatency();

        latency.Stalled(Ms(dryMs));

        latency.ExtraMs.ShouldBe(expectedExtraMs);
    }

    [Fact]
    public void EachStall_AddsToThePreviousOnes()
    {
        var latency = new AdaptiveLatency();

        latency.Stalled(Ms(100));
        Play(latency, 10);
        latency.Stalled(Ms(20));

        latency.ExtraMs.ShouldBe(140 + 60);
    }

    [Fact]
    public void Outage_IsNotLearntFrom()
    {
        var latency = new AdaptiveLatency();
        latency.Stalled(Ms(100));

        latency.Stalled(AdaptiveLatency.Outage + Ms(1)); // the broadcast stopped, then came back

        latency.ExtraMs.ShouldBe(140);
    }

    [Fact]
    public void Extra_NeverExceedsItsMaximum()
    {
        var latency = new AdaptiveLatency();

        for (int i = 0; i < 10; i++)
            latency.Stalled(Ms(300));

        latency.ExtraMs.ShouldBe(AdaptiveLatency.MaxExtraMs);
    }

    [Fact]
    public void LongCalm_GivesItBackOnePacketAtATime()
    {
        var latency = new AdaptiveLatency();
        latency.Stalled(Ms(1));

        Play(latency, AdaptiveLatency.CalmFramesPerStepDown);
        latency.ExtraMs.ShouldBe(60 - PlayoutTuning.FrameMs);

        Play(latency, 10 * AdaptiveLatency.CalmFramesPerStepDown);
        latency.ExtraMs.ShouldBe(0);
    }

    [Fact]
    public void Stall_RestartsTheCalmPeriod()
    {
        var latency = new AdaptiveLatency();
        latency.Stalled(Ms(1));
        Play(latency, AdaptiveLatency.CalmFramesPerStepDown - 1);

        latency.Stalled(Ms(1));
        Play(latency, AdaptiveLatency.CalmFramesPerStepDown - 1);

        latency.ExtraMs.ShouldBe(120);
    }
}
