using AuraMusic.Kernel.Playout;
using Moq;
using Shouldly;

namespace AuraMusic.Kernel.Tests.Playout;

public sealed class PlayoutControllerTests
{
    readonly Mock<IPlayoutMetrics> metrics = new();
    readonly byte[] packet = [1, 2, 3];

    // Expression trees (ShouldAllBe) cannot hold an 'is' pattern.
    static bool IsConceal(PlayoutStep step) => step is Conceal;

    PlayoutController CreateController() => new(metrics.Object) { MaxBacklogFrames = 8, MaxConcealedFrames = 3 };

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
    public void Next_BacklogAtMax_StillPlays()
    {
        var step = CreateController().Next(packet, backlog: 8);

        (step is Play).ShouldBeTrue();
    }

    [Fact]
    public void Defaults_StartWith100MsInHand()
    {
        new PlayoutController(metrics.Object).PrebufferFrames.ShouldBe(5);
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
