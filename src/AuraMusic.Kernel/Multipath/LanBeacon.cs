namespace AuraMusic.Kernel.Multipath;

/// <summary>
/// "An AuraMusic master is here": broadcast every second over UDP on the local network, so listeners on the
/// same Wi-Fi find the master's TCP port without Android's ever-changing service discovery APIs.
/// </summary>
public sealed record LanBeacon(uint Session, int Port, string Name)
{
    /// <summary>UDP port both sides use for the beacon.</summary>
    public const int UdpPort = 47_011;

    static ReadOnlySpan<byte> Magic => "AURB"u8;
    const byte Version = 1;

    public byte[] Encode()
    {
        var name = AuraProtocol.Truncate(Name);
        var datagram = new byte[4 + 1 + 4 + 2 + 1 + name.Length];
        Magic.CopyTo(datagram);
        datagram[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(5), Session);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(9), (ushort)Port);
        datagram[11] = (byte)name.Length;
        name.CopyTo(datagram, 12);
        return datagram;
    }

    /// <summary>Anything else on the network that happens to use the port is simply ignored.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> datagram, [NotNullWhen(true)] out LanBeacon? beacon)
    {
        beacon = null;
        if (datagram.Length < 12 || !datagram[..4].SequenceEqual(Magic) || datagram[4] != Version)
            return false;
        int nameLength = datagram[11];
        if (nameLength > AuraProtocol.MaxNameBytes || datagram.Length < 12 + nameLength)
            return false;
        int port = BinaryPrimitives.ReadUInt16LittleEndian(datagram[9..]);
        if (port == 0)
            return false;
        beacon = new LanBeacon(
            BinaryPrimitives.ReadUInt32LittleEndian(datagram[5..]),
            port,
            Encoding.UTF8.GetString(datagram.Slice(12, nameLength)));
        return true;
    }
}
