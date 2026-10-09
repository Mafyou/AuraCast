namespace AuraMusic.Kernel.Tests.Playout;

public sealed class PlayoutControllerTests
{
    readonly Mock<IPlayoutMetrics> metrics = new();
    readonly byte[] packet = [1, 2, 3];

    // Expression trees (ShouldAllBe) cannot hold an 'is' pattern.
    static bool IsConceal(PlayoutStep step) => step is Conceal;

    static bool IsCatchUp(PlayoutStep step) => step is CatchUp;

    PlayoutController CreateController() =>
        new(metrics.Object) { CatchUpAboveFrames = 4, CatchUpEveryFrames = 1, MaxBacklogFrames = 8, MaxConcealedFrames = 3 };

    [Fact]
    public void Next_PacketOnTime_PlaysIt()
    {
        var step = CreateController().Next(packet, backlog: 2);

        var play = step is Play p ? p : null;
        play.ShouldNotBeNull().Packet.ShouldBeSameAs(packet);
        metrics.VerifyNoOtherCalls();
    }

    [Fact]
    public void Next_PacketLate_ConcealsAndReportsIt()
    {
        var step = CreateController().Next(null, backlog: 0);

        (step is Conceal).ShouldBeTrue();
        metrics.Verify(m => m.Concealed(), Times.Once);
        metrics.VerifyNoOtherCalls();
    }

    [Fact]
    public void Next_MoreLatePacketsThanAllowed_Rebuffers()
    {
        var controller = CreateController();

        var steps = Enumerable.Range(0, 4).Select(_ => controller.Next(null, backlog: 0)).ToList();

        steps.Take(3).ShouldAllBe(step => IsConceal(step));
        (steps[3] is Rebuffer).ShouldBeTrue();
        metrics.Verify(m => m.Concealed(), Times.Exactly(3));
        metrics.Verify(m => m.Rebuffered(), Times.Once);
    }

    [Fact]
    public void Next_AfterRebuffer_ConcealsAgainBeforeTheNextRebuffer()
    {
        var controller = CreateController();
        for (int i = 0; i < 4; i++)
            controller.Next(null, backlog: 0);

        var step = controller.Next(null, backlog: 0);

        (step is Conceal).ShouldBeTrue();
    }

    [Fact]
    public void Next_PacketBetweenLateOnes_ResetsTheConcealedCount()
    {
        var controller = CreateController();
        for (int i = 0; i < 3; i++)
            controller.Next(null, backlog: 0);
        controller.Next(packet, backlog: 0);

        var steps = Enumerable.Range(0, 3).Select(_ => controller.Next(null, backlog: 0));

        steps.ShouldAllBe(step => IsConceal(step));
        metrics.Verify(m => m.Rebuffered(), Times.Never);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(14)]
    public void Next_BacklogAboveMax_SkipsThePacket(int backlog)
    {
        var step = CreateController().Next(packet, backlog);

        var skip = step is Skip s ? s : null;
        skip.ShouldNotBeNull().Packet.ShouldBeSameAs(packet);
        metrics.Verify(m => m.Skipped(), Times.Once);
    }

    [Fact]
    public void Next_BacklogAtMax_CatchesUpInsteadOfSkipping()
    {
        var step = CreateController().Next(packet, backlog: 8);

        var catchUp = step is CatchUp c ? c : null;
        catchUp.ShouldNotBeNull().Packet.ShouldBeSameAs(packet);
        metrics.Verify(m => m.CaughtUp(), Times.Once);
        metrics.Verify(m => m.Skipped(), Times.Never);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Next_SlightlyBehind_CatchesUp(int backlog)
    {
        (CreateController().Next(packet, backlog) is CatchUp).ShouldBeTrue();
    }

    [Fact]
    public void Next_BacklogAtCatchUpThreshold_Plays()
    {
        (CreateController().Next(packet, backlog: 4) is Play).ShouldBeTrue();
        metrics.VerifyNoOtherCalls();
    }

    [Fact]
    public void Defaults_RideOutStalls_AndNeverSoundMetallic()
    {
        var controller = new PlayoutController(metrics.Object);

        controller.PrebufferFrames.ShouldBe(10);     // 200 ms in hand
        controller.CatchUpAboveFrames.ShouldBe(16);  // catch up past 320 ms
        controller.CatchUpEveryFrames.ShouldBe(5);   // 1 ms per 100 ms
        controller.MaxBacklogFrames.ShouldBe(30);    // drop only past 600 ms
        controller.MaxConcealedFrames.ShouldBe(4);   // 80 ms of concealment at most
    }

    [Fact]
    public void Next_CatchingUp_ShortensOnlyOnePacketInFive()
    {
        var controller = new PlayoutController(metrics.Object) { CatchUpAboveFrames = 4, CatchUpEveryFrames = 5, MaxBacklogFrames = 50 };

        var steps = Enumerable.Range(0, 10).Select(_ => controller.Next(packet, backlog: 10)).ToList();

        steps.Count(step => IsCatchUp(step)).ShouldBe(2);
        (steps[0] is CatchUp).ShouldBeTrue();
        (steps[5] is CatchUp).ShouldBeTrue();
        metrics.Verify(m => m.CaughtUp(), Times.Exactly(2));
    }

    [Fact]
    public void CatchUpEveryFrames_NotPositive_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PlayoutController(metrics.Object) { CatchUpEveryFrames = 0 });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PrebufferFrames_NotPositive_Throws(int frames)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PlayoutController(metrics.Object) { PrebufferFrames = frames })
            .ParamName.ShouldBe(nameof(PlayoutController.PrebufferFrames));
    }

    [Fact]
    public void MaxConcealedFrames_Negative_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PlayoutController(metrics.Object) { MaxConcealedFrames = -1 });
    }
}
