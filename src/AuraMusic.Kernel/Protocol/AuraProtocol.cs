namespace AuraMusic.Kernel.Protocol;

/// <summary>
/// Wire format, the same over Bluetooth RFCOMM and Wi-Fi TCP:
/// the master sends a header ("AURA", version, session id), the listener answers with a <see cref="Hello"/>,
/// then come length-prefixed, numbered Opus packets of 20 ms each, and empty keep-alive frames when there is
/// no sound. The session id and the sequence numbers let a listener connected over both links keep the first
/// copy of every packet and drop the other.
/// </summary>
public static class AuraProtocol
{
    /// <summary>SDP service record the master listens on; listeners look for it on their paired phones.</summary>
    public static readonly Guid ServiceUuid = new("6a1d0c5e-3b7f-4e2a-9c41-a0ca57000001");

    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int FrameSamples = SampleRate / 50; // 20 ms per channel
    // Robustness first: very good Opus stereo music for half the airtime of 192 kbps, so even a busy
    // Bluetooth radio (headphones, distance, several listeners) keeps up.
    public const int Bitrate = 96_000;
    /// <summary>Used while every listener is served by healthy Wi-Fi, where airtime is not a concern.</summary>
    public const int WifiBitrate = 160_000;
    public const int MaxPacketSize = 1275;
    public const int MaxNameBytes = 64;

    const byte Version = 3;
    const int HeaderSize = 4 + 1 + 4;
    const int FramePrefixSize = 2 + 4;
    static ReadOnlySpan<byte> Magic => "AURA"u8;

    public static void WriteHeader(Stream stream, uint session)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        header[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(header[5..], session);
        stream.Write(header);
        stream.Flush();
    }

    /// <returns>The session id of the broadcast.</returns>
    public static uint ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header[..5]);
        if (!header[..4].SequenceEqual(Magic))
            throw new InvalidDataException(KernelStrings.NotAuraStream);
        if (header[4] != Version)
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, KernelStrings.IncompatibleVersion, header[4], Version));
        stream.ReadExactly(header[5..]);
        return BinaryPrimitives.ReadUInt32LittleEndian(header[5..]);
    }

    public static void WriteFrame(Stream stream, uint sequence, ReadOnlySpan<byte> packet)
    {
        Span<byte> frame = stackalloc byte[FramePrefixSize + packet.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame[2..], sequence);
        packet.CopyTo(frame[FramePrefixSize..]);
        stream.Write(frame);
        stream.Flush();
    }

    /// <returns>The packet length written into <paramref name="packet"/>.</returns>
    public static int ReadFrame(Stream stream, Span<byte> packet, out uint sequence)
    {
        Span<byte> prefix = stackalloc byte[FramePrefixSize];
        stream.ReadExactly(prefix);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        sequence = BinaryPrimitives.ReadUInt32LittleEndian(prefix[2..]);
        if (size > packet.Length)
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, KernelStrings.FrameTooLarge, size));
        stream.ReadExactly(packet[..size]);
        return size;
    }

    /// <summary>
    /// An empty frame, sent about once a second while there is no sound (music paused), so both ends can tell
    /// a quiet link from a dead one. <see cref="ReadFrame"/> returns 0 for it.
    /// </summary>
    public static void WriteKeepAlive(Stream stream) => WriteFrame(stream, 0, []);

    /// <summary>Sent by a listener on every link right after the header, so the master knows which phone it is.</summary>
    public static void WriteHello(Stream stream, Hello hello)
    {
        var name = Truncate(hello.Name);
        Span<byte> bytes = stackalloc byte[16 + 1 + name.Length];
        hello.Id.TryWriteBytes(bytes);
        bytes[16] = (byte)name.Length;
        name.CopyTo(bytes[17..]);
        stream.Write(bytes);
        stream.Flush();
    }

    public static Hello ReadHello(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[17];
        stream.ReadExactly(prefix);
        if (prefix[16] > MaxNameBytes)
            throw new InvalidDataException(KernelStrings.NotAuraStream);
        Span<byte> name = stackalloc byte[prefix[16]];
        stream.ReadExactly(name);
        return new Hello(new Guid(prefix[..16]), Encoding.UTF8.GetString(name));
    }

    /// <summary>UTF-8 bytes of <paramref name="name"/>, cut to <see cref="MaxNameBytes"/> on a character boundary.</summary>
    internal static byte[] Truncate(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        if (bytes.Length <= MaxNameBytes)
            return bytes;
        int cut = MaxNameBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) // do not split a multi-byte character
            cut--;
        return bytes[..cut];
    }
}

/// <param name="Id">Stable per installation: the same phone is recognised over Bluetooth and over Wi-Fi.</param>
/// <param name="Name">What the master shows.</param>
public sealed record Hello(Guid Id, string Name);
