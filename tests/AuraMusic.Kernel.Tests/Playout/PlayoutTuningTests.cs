namespace AuraMusic.Kernel.Tests.Playout;

public sealed class PlayoutTuningTests : IDisposable
{
    public void Dispose() => PlayoutTuning.TargetLatencyMs = PlayoutTuning.DefaultLatencyMs;

    [Theory]
    [InlineData(200, 200)]
    [InlineData(10, PlayoutTuning.MinLatencyMs)]
    [InlineData(9_000, PlayoutTuning.MaxLatencyMs)]
    public void TargetLatency_StaysWithinItsRange(int requested, int expected)
    {
        PlayoutTuning.TargetLatencyMs = requested;

        PlayoutTuning.TargetLatencyMs.ShouldBe(expected);
    }

    [Theory]
    [InlineData(200, 7)]
    [InlineData(80, 1)]
    [InlineData(500, 22)]
    [InlineData(60, 1)]
    public void CushionFrames_LeaveTheOutputItsShare(int latencyMs, int expectedFrames)
    {
        PlayoutTuning.CushionFrames(latencyMs).ShouldBe(expectedFrames);
    }

    [Fact]
    public void SetCushion_MovesTheThresholdsTogether()
    {
        var controller = new PlayoutController(Mock.Of<IPlayoutMetrics>());

        controller.SetCushion(3);

        controller.PrebufferFrames.ShouldBe(3);
        controller.CatchUpAboveFrames.ShouldBe(3 + PlayoutTuning.CatchUpMarginFrames);
        controller.MaxBacklogFrames.ShouldBe(3 + PlayoutTuning.SkipMarginFrames);
    }
}
