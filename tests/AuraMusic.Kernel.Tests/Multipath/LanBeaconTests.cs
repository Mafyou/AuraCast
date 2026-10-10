namespace AuraMusic.Kernel.Tests.Multipath;

public sealed class LanBeaconTests
{
    [Fact]
    public void Beacon_RoundTrips()
    {
        var beacon = new LanBeacon(Session: 0xDEAD_BEEF, Port: 51_234, Name: "Téléphone de Mathieu");

        LanBeacon.TryDecode(beacon.Encode(), out var decoded).ShouldBeTrue();

        decoded.ShouldBe(beacon);
    }

    [Fact]
    public void LongName_IsCut()
    {
        var beacon = new LanBeacon(1, 4000, new string('x', 200));

        LanBeacon.TryDecode(beacon.Encode(), out var decoded).ShouldBeTrue();

        decoded.Name.Length.ShouldBe(AuraProtocol.MaxNameBytes);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { (byte)'A', (byte)'U', (byte)'R', (byte)'A', 2, 0, 0, 0, 0, 1, 0, 0 })] // a stream header, not a beacon
    [InlineData(new byte[] { (byte)'A', (byte)'U', (byte)'R', (byte)'B', 9, 0, 0, 0, 0, 1, 0, 0 })] // unknown version
    public void Garbage_IsIgnored(byte[] datagram)
    {
        LanBeacon.TryDecode(datagram, out var beacon).ShouldBeFalse();
        beacon.ShouldBeNull();
    }

    [Fact]
    public void TruncatedName_IsIgnored()
    {
        var datagram = new LanBeacon(1, 4000, "Nord 2").Encode();

        LanBeacon.TryDecode(datagram.AsSpan(0, datagram.Length - 2), out _).ShouldBeFalse();
    }

    [Fact]
    public void PortZero_IsIgnored()
    {
        var datagram = new LanBeacon(1, 4000, "Nord 2").Encode();
        datagram[9] = datagram[10] = 0;

        LanBeacon.TryDecode(datagram, out _).ShouldBeFalse();
    }
}
