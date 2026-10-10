namespace AuraMusic.Kernel.Tests.Protocol;

public sealed class AuraProtocolTests
{
    [Fact]
    public void FrameSamples_Are20Milliseconds()
    {
        (AuraProtocol.FrameSamples * 1000 / AuraProtocol.SampleRate).ShouldBe(20);
    }

    [Fact]
    public void Header_RoundTripsTheSession()
    {
        using var stream = new MemoryStream();

        AuraProtocol.WriteHeader(stream, session: 0xCAFE_F00D);
        stream.Position = 0;

        AuraProtocol.ReadHeader(stream).ShouldBe(0xCAFE_F00Du);
        stream.Position.ShouldBe(stream.Length);
    }

    [Fact]
    public void ReadHeader_WrongMagic_Throws()
    {
        using var stream = new MemoryStream("NOPE\u0002\0\0\0\0"u8.ToArray());

        Should.Throw<InvalidDataException>(() => AuraProtocol.ReadHeader(stream));
    }

    [Fact]
    public void ReadHeader_OtherVersion_ThrowsWithAClearMessage()
    {
        using var stream = new MemoryStream([.. "AURA"u8, 1]);

        Should.Throw<InvalidDataException>(() => AuraProtocol.ReadHeader(stream)).Message.ShouldContain("1");
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
    public void Frame_RoundTripsPacketAndSequence(int size)
    {
        var packet = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        using var stream = new MemoryStream();

        AuraProtocol.WriteFrame(stream, sequence: 4_000_000_000, packet);
        stream.Position = 0;
        var buffer = new byte[AuraProtocol.MaxPacketSize];
        int length = AuraProtocol.ReadFrame(stream, buffer, out uint sequence);

        length.ShouldBe(size);
        sequence.ShouldBe(4_000_000_000u);
        buffer[..length].ShouldBe(packet);
    }

    [Fact]
    public void Frames_AreReadBackInOrder()
    {
        using var stream = new MemoryStream();
        AuraProtocol.WriteFrame(stream, 7, [1, 1]);
        AuraProtocol.WriteFrame(stream, 8, [2, 2, 2]);
        stream.Position = 0;
        var buffer = new byte[AuraProtocol.MaxPacketSize];

        AuraProtocol.ReadFrame(stream, buffer, out uint first).ShouldBe(2);
        AuraProtocol.ReadFrame(stream, buffer, out uint second).ShouldBe(3);
        (first, second).ShouldBe((7u, 8u));
        buffer[..3].ShouldBe(new byte[] { 2, 2, 2 });
    }

    [Fact]
    public void ReadFrame_LargerThanBuffer_Throws()
    {
        using var stream = new MemoryStream();
        AuraProtocol.WriteFrame(stream, 1, new byte[100]);
        stream.Position = 0;

        Should.Throw<InvalidDataException>(() => AuraProtocol.ReadFrame(stream, new byte[10], out _));
    }

    [Fact]
    public void ReadFrame_TruncatedPayload_Throws()
    {
        using var stream = new MemoryStream();
        AuraProtocol.WriteFrame(stream, 1, new byte[100]);
        stream.SetLength(50);
        stream.Position = 0;

        Should.Throw<EndOfStreamException>(() => AuraProtocol.ReadFrame(stream, new byte[AuraProtocol.MaxPacketSize], out _));
    }

    [Theory]
    [InlineData("Nord 2")]
    [InlineData("Téléphone de Léa 🎧")]
    [InlineData("")]
    public void Hello_RoundTripsTheName(string name)
    {
        using var stream = new MemoryStream();

        AuraProtocol.WriteHello(stream, name);
        stream.Position = 0;

        AuraProtocol.ReadHello(stream).ShouldBe(name);
    }

    [Fact]
    public void Hello_LongName_IsCutWithoutBreakingACharacter()
    {
        string name = new string('é', 40); // 80 UTF-8 bytes
        using var stream = new MemoryStream();

        AuraProtocol.WriteHello(stream, name);
        stream.Position = 0;
        var read = AuraProtocol.ReadHello(stream);

        Encoding.UTF8.GetByteCount(read).ShouldBeLessThanOrEqualTo(AuraProtocol.MaxNameBytes);
        name.ShouldStartWith(read);
    }
}
