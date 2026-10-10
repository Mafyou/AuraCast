namespace AuraMusic.Kernel.Tests.Playout;

public sealed class DriftControllerTests
{
    const int Nominal = 48_000;

    static DriftController Controller(double targetMs = 200) => new(Nominal) { TargetMs = targetMs };

    [Fact]
    public void OnTarget_PlaysAtTheNominalRate()
    {
        Controller().Update(200).ShouldBe(Nominal);
    }

    [Fact]
    public void TooMuchBuffered_PlaysFaster()
    {
        var controller = Controller();

        int rate = 0;
        for (int i = 0; i < 200; i++)
            rate = controller.Update(300);

        rate.ShouldBeGreaterThan(Nominal);
        controller.Correction.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void TooLittleBuffered_PlaysSlower()
    {
        var controller = Controller();

        int rate = 0;
        for (int i = 0; i < 200; i++)
            rate = controller.Update(100);

        rate.ShouldBeLessThan(Nominal);
    }

    [Theory]
    [InlineData(5_000)]
    [InlineData(-5_000)]
    public void Correction_NeverExceedsItsLimit(double offsetMs)
    {
        var controller = Controller();

        for (int i = 0; i < 5_000; i++)
            controller.Update(200 + offsetMs);

        Math.Abs(controller.Correction).ShouldBeLessThanOrEqualTo(DriftController.MaxCorrection);
        Math.Abs(controller.Update(200 + offsetMs) - Nominal).ShouldBeLessThanOrEqualTo((int)(Nominal * DriftController.MaxCorrection) + 1);
    }

    [Fact]
    public void OnePacketOfJitter_BarelyMovesTheRate()
    {
        var controller = Controller();
        for (int i = 0; i < 100; i++)
            controller.Update(200);

        int rate = controller.Update(220); // a packet arrived early: not a trend

        Math.Abs(rate - Nominal).ShouldBeLessThanOrEqualTo(1);
    }

    /// <summary>
    /// The listener's hardware clock is off by <paramref name="clockErrorPpm"/>: without correction the buffer
    /// would wander by 0.2 ms a second per 200 ppm, for ever.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(-200)]
    [InlineData(50)]
    public void ClockDifference_IsAbsorbed_TheBufferStaysOnTarget(double clockErrorPpm)
    {
        var controller = Controller();
        double buffered = 200;

        for (int packet = 0; packet < 50 * 600; packet++) // ten minutes
        {
            int rate = controller.Update(buffered);
            double played = 20.0 * rate / Nominal * (1 + clockErrorPpm / 1e6);
            buffered += 20 - played; // 20 ms arrive, a bit more or less is played
        }

        buffered.ShouldBe(200, tolerance: 5);
    }

    [Fact]
    public void AfterABurst_TheSurplusIsPlayedOffWithoutSkipping()
    {
        var controller = Controller();
        double buffered = 320; // a stall then a burst left 120 ms too much

        for (int packet = 0; packet < 50 * 180; packet++) // three minutes
            buffered += 20 - 20.0 * controller.Update(buffered) / Nominal;

        buffered.ShouldBe(200, tolerance: 10);
    }

    [Fact]
    public void NewTarget_IsFollowed()
    {
        var controller = Controller();
        double buffered = 200;
        controller.TargetMs = 120; // the slider moved towards "closer to the master"

        for (int packet = 0; packet < 50 * 180; packet++)
            buffered += 20 - 20.0 * controller.Update(buffered) / Nominal;

        buffered.ShouldBe(120, tolerance: 10);
    }

    [Fact]
    public void Restart_ForgetsTheOldLevelNotTheTarget()
    {
        var controller = Controller();
        for (int i = 0; i < 500; i++)
            controller.Update(400);

        controller.Restart();
        controller.Update(200);

        controller.SmoothedMs.ShouldBe(200, tolerance: 0.001);
        controller.TargetMs.ShouldBe(200);
    }
}
