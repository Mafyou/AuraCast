using System.Buffers.Binary;
using System.Globalization;
using AuraCast.Kernel.Resources;

namespace AuraCast.Kernel.Protocol;

/// <summary>
/// Wire format over the Bluetooth RFCOMM socket:
/// a header ("AURA" + version), then length-prefixed Opus packets of 20 ms each.
/// </summary>
public static class AuraProtocol
{
    /// <summary>SDP service record the master listens on; listeners look for it on their paired phones.</summary>
    public static readonly Guid ServiceUuid = new("6a1d0c5e-3b7f-4e2a-9c41-a0ca57000001");

    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int FrameSamples = SampleRate / 50; // 20 ms per channel
    public const int Bitrate = 128_000; // transparent quality, a fraction of what RFCOMM carries
    public const int MaxPacketSize = 1275;

    const byte Version = 1;
    static ReadOnlySpan<byte> Magic => "AURA"u8;

    public static void WriteHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[5];
        Magic.CopyTo(header);
        header[4] = Version;
        stream.Write(header);
        stream.Flush();
    }

    public static void ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[5];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual(Magic))
            throw new InvalidDataException(KernelStrings.NotAuraStream);
        if (header[4] != Version)
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, KernelStrings.IncompatibleVersion, header[4], Version));
    }

    public static void WriteFrame(Stream stream, ReadOnlySpan<byte> packet)
    {
        Span<byte> frame = stackalloc byte[2 + packet.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)packet.Length);
        packet.CopyTo(frame[2..]);
        stream.Write(frame);
        stream.Flush();
    }

    /// <returns>The packet length written into <paramref name="packet"/>.</returns>
    public static int ReadFrame(Stream stream, Span<byte> packet)
    {
        Span<byte> length = stackalloc byte[2];
        stream.ReadExactly(length);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(length);
        if (size > packet.Length)
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, KernelStrings.FrameTooLarge, size));
        stream.ReadExactly(packet[..size]);
        return size;
    }
}
