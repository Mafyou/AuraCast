namespace AuraMusic.Kernel.Tests.Protocol;

public sealed class AuraProtocolTests
{
    [Fact]
    public void FrameSamples_Are20Milliseconds()
    {
        (AuraProtocol.FrameSamples * 1000 / AuraProtocol.SampleRate).ShouldBe(20);
    }

    [Fact]
    public void Header_RoundTrips()
    {
        using var stream = new MemoryStream();

        AuraProtocol.WriteHeader(stream);
        stream.Position = 0;

        Should.NotThrow(() => AuraProtocol.ReadHeader(stream));
        stream.Position.ShouldBe(stream.Length);
    }

    [Fact]
    public void ReadHeader_WrongMagic_Throws()
    {
        using var stream = new MemoryStream("NOPE\u0001"u8.ToArray());

        Should.Throw<InvalidDataException>(() => AuraProtocol.ReadHeader(stream));
    }

    [Fact]
    public void ReadHeader_OtherVersion_ThrowsWithAClearMessage()
    {
        using var stream = new MemoryStream([.. "AURA"u8, 99]);

        Should.Throw<InvalidDataException>(() => AuraProtocol.ReadHeader(stream)).Message.ShouldContain("99");
    }

    [Fact]
    public void ReadHeader_TruncatedStream_Throws()
    {
        using var stream = new MemoryStream("AU"u8.ToArray());

        Should.Throw<EndOfStreamException>(() => AuraProtocol.ReadHeader(stream));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(320)]
    [InlineData(AuraProtocol.MaxPacketSize)]
    public void Frame_RoundTrips(int size)
    {
        var packet = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        using var stream = new MemoryStream();

        AuraProtocol.WriteFrame(stream, packet);
        stream.Position = 0;
        var buffer = new byte[AuraProtocol.MaxPacketSize];
        int length = AuraProtocol.ReadFrame(stream, buffer);

        length.ShouldBe(size);
        buffer[..length].ShouldBe(packet);
    }

    [Fact]
    public void Frames_AreReadBackInOrder()
    {
        using var stream = new MemoryStream();
        AuraProtocol.WriteFrame(stream, [1, 1]);
        AuraProtocol.WriteFrame(stream, [2, 2, 2]);
        stream.Position = 0;
        var buffer = new byte[AuraProtocol.MaxPacketSize];

        AuraProtocol.ReadFrame(stream, buffer).ShouldBe(2);
        AuraProtocol.ReadFrame(stream, buffer).ShouldBe(3);
        buffer[..3].ShouldBe(new byte[] { 2, 2, 2 });
    }

    [Fact]
    public void ReadFrame_LargerThanBuffer_Throws()
    {
        using var stream = new MemoryStream();
        AuraProtocol.WriteFrame(stream, new byte[100]);
        stream.Position = 0;

        Should.Throw<InvalidDataException>(() => AuraProtocol.ReadFrame(stream, new byte[10]));
    }

    [Fact]
    public void ReadFrame_TruncatedPayload_Throws()
    {
        using var stream = new MemoryStream();
        AuraProtocol.WriteFrame(stream, new byte[100]);
        stream.SetLength(50);
        stream.Position = 0;

        Should.Throw<EndOfStreamException>(() => AuraProtocol.ReadFrame(stream, new byte[AuraProtocol.MaxPacketSize]));
    }
}
