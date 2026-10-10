namespace AuraMusic.Kernel.Tests.Playout;

public sealed class AdaptiveLatencyTests
{
    static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(300);

    static void Play(AdaptiveLatency latency, int frames)
    {
        for (int i = 0; i < frames; i++)
            latency.Played();
    }

    [Fact]
    public void CleanLink_AddsNothing()
    {
        var latency = new AdaptiveLatency();

        Play(latency, 3 * AdaptiveLatency.CalmFramesPerStepDown);

        latency.ExtraMs.ShouldBe(0);
    }

    [Fact]
    public void RunningDry_RaisesTheLatencyOnce_HoweverManyPacketsAreMissing()
    {
        var latency = new AdaptiveLatency();

        latency.RanDry();
        latency.RanDry();
        latency.RanDry();

        latency.ExtraMs.ShouldBe(AdaptiveLatency.StepMs);
    }

    [Fact]
    public void EachNewDryRun_RaisesItAgain()
    {
        var latency = new AdaptiveLatency();

        latency.RanDry();
        Play(latency, 10);
        latency.RanDry();

        latency.ExtraMs.ShouldBe(2 * AdaptiveLatency.StepMs);
    }

    [Fact]
    public void Rebuffer_AfterAShortWait_RaisesItFurther()
    {
        var latency = new AdaptiveLatency();

        latency.RanDry();
        latency.Rebuffered(ShortWait);

        latency.ExtraMs.ShouldBe(2 * AdaptiveLatency.StepMs);
    }

    [Fact]
    public void Rebuffer_AfterAnOutage_TakesBackWhatThatRunAdded()
    {
        var latency = new AdaptiveLatency();
        latency.RanDry();
        Play(latency, 10);

        latency.RanDry(); // the broadcast stops...
        latency.Rebuffered(AdaptiveLatency.Outage + TimeSpan.FromSeconds(1)); // ...and comes back later

        latency.ExtraMs.ShouldBe(AdaptiveLatency.StepMs);
    }

    [Fact]
    public void Extra_NeverExceedsItsMaximum()
    {
        var latency = new AdaptiveLatency();

        for (int i = 0; i < 50; i++)
        {
            latency.RanDry();
            latency.Rebuffered(ShortWait);
            Play(latency, 10);
        }

        latency.ExtraMs.ShouldBe(AdaptiveLatency.MaxExtraMs);
    }

    [Fact]
    public void LongCalm_GivesItBackOnePacketAtATime()
    {
        var latency = new AdaptiveLatency();
        latency.RanDry();

        Play(latency, AdaptiveLatency.CalmFramesPerStepDown);
        latency.ExtraMs.ShouldBe(AdaptiveLatency.StepMs - PlayoutTuning.FrameMs);

        Play(latency, 10 * AdaptiveLatency.CalmFramesPerStepDown);
        latency.ExtraMs.ShouldBe(0);
    }

    [Fact]
    public void RunningDry_RestartsTheCalmPeriod()
    {
        var latency = new AdaptiveLatency();
        latency.RanDry();
        Play(latency, AdaptiveLatency.CalmFramesPerStepDown - 1);

        latency.RanDry();
        Play(latency, AdaptiveLatency.CalmFramesPerStepDown - 1);

        latency.ExtraMs.ShouldBe(2 * AdaptiveLatency.StepMs);
    }
}
