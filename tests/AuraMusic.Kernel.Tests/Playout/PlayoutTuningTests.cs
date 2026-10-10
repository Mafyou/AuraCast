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
    [InlineData(100, 2)]
    [InlineData(500, 22)]
    [InlineData(60, PlayoutTuning.MinCushionFrames)]
    public void CushionFrames_LeaveTheOutputItsShare(int latencyMs, int expectedFrames)
    {
        PlayoutTuning.CushionFrames(latencyMs).ShouldBe(expectedFrames);
    }

    [Theory]
    [InlineData(200, 60, 200)]
    [InlineData(100, 60, 100)]
    [InlineData(100, 120, 160)] // the phone's output holds 120 ms whatever is asked: two packets on top, no less
    [InlineData(200, 120, 200)]
    public void ReachableLatency_NeverAimsBelowWhatThePhoneCanDo(int latencyMs, int outputMs, int expectedMs)
    {
        PlayoutTuning.ReachableLatencyMs(latencyMs, outputMs).ShouldBe(expectedMs);
    }

    [Theory]
    [InlineData(Links.Bluetooth, 200, PlayoutTuning.BluetoothMinLatencyMs)] // bursts and radio scans need more in hand
    [InlineData(Links.Bluetooth, 500, 500)]
    [InlineData(Links.Wifi, 200, 200)]
    [InlineData(Links.Wifi | Links.Bluetooth, 100, 100)]
    [InlineData(Links.None, 200, 200)]
    public void LatencyFor_RaisesTheSettingOnBluetoothAlone(Links links, int setting, int expectedMs)
    {
        PlayoutTuning.TargetLatencyMs = setting;

        PlayoutTuning.LatencyFor(links).ShouldBe(expectedMs);
    }

    [Fact]
    public void MinLatency_IsReachableWithTheUsualOutput()
    {
        PlayoutTuning.ReachableLatencyMs(PlayoutTuning.MinLatencyMs).ShouldBe(PlayoutTuning.MinLatencyMs);
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
