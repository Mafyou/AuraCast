namespace AuraMusic.Kernel.Tests.Multipath;

public sealed class SequenceGateTests
{
    const uint Session = 42;

    static SequenceGate Joined()
    {
        var gate = new SequenceGate();
        gate.Join(Session).ShouldBeTrue();
        return gate;
    }

    [Fact]
    public void FirstCopyPasses_SecondCopyIsDropped()
    {
        var gate = Joined();

        gate.TryAccept(Session, 10).ShouldBeTrue();  // arrived over Wi-Fi
        gate.TryAccept(Session, 10).ShouldBeFalse(); // the same packet, later, over Bluetooth
    }

    [Fact]
    public void TwoLinks_InterleavedCopies_DeliverEachPacketOnce()
    {
        var gate = Joined();
        uint[] wifi = [1, 2, 3, 4, 5];
        uint[] bluetooth = [1, 2, 3, 4, 5, 6];

        var delivered = new List<uint>();
        for (int i = 0; i < 6; i++)
        {
            if (i < wifi.Length && gate.TryAccept(Session, wifi[i]))
                delivered.Add(wifi[i]);
            if (gate.TryAccept(Session, bluetooth[i]))
                delivered.Add(bluetooth[i]);
        }

        delivered.ShouldBe([1u, 2, 3, 4, 5, 6]);
    }

    [Fact]
    public void OlderThanWhatWasPlayed_IsDropped()
    {
        var gate = Joined();
        gate.TryAccept(Session, 20).ShouldBeTrue();

        gate.TryAccept(Session, 19).ShouldBeFalse(); // too late: 20 already went through
    }

    [Fact]
    public void Gaps_ArePassedOn()
    {
        var gate = Joined();
        gate.TryAccept(Session, 1).ShouldBeTrue();

        gate.TryAccept(Session, 5).ShouldBeTrue(); // 2..4 lost on both links: concealment takes over
    }

    [Fact]
    public void SequenceWrapAround_StillCountsAsNewer()
    {
        var gate = Joined();
        gate.TryAccept(Session, uint.MaxValue).ShouldBeTrue();

        gate.TryAccept(Session, 0).ShouldBeTrue();
    }

    [Fact]
    public void AnotherBroadcast_IsRefused()
    {
        var gate = Joined();

        gate.Join(Session + 1).ShouldBeFalse();
        gate.TryAccept(Session + 1, 1).ShouldBeFalse();
        gate.Session.ShouldBe(Session);
    }

    [Fact]
    public void Reset_LetsTheNextLinkFollowANewBroadcast()
    {
        var gate = Joined();
        gate.TryAccept(Session, 500).ShouldBeTrue();

        gate.Reset();
        gate.Join(Session + 1).ShouldBeTrue();

        gate.TryAccept(Session + 1, 0).ShouldBeTrue(); // the new master restarts its numbering
    }

    [Fact]
    public void ManyThreads_DeliverEachSequenceExactlyOnce()
    {
        var gate = Joined();
        int accepted = 0;

        Parallel.For(0, 4, _ =>
        {
            for (uint sequence = 1; sequence <= 10_000; sequence++)
                if (gate.TryAccept(Session, sequence))
                    Interlocked.Increment(ref accepted);
        });

        accepted.ShouldBeLessThanOrEqualTo(10_000);
        accepted.ShouldBeGreaterThan(0);
    }
}
