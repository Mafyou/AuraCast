namespace AuraMusic.Kernel.Codec;

/// <summary>Turns 20 ms of interleaved 16-bit PCM into one Opus packet.</summary>
public interface IAudioEncoder : IDisposable
{
    /// <summary><see langword="true"/> for libopus, <see langword="false"/> for the managed fallback.</summary>
    bool IsNative { get; }

    /// <summary>Bits per second; can change between two packets.</summary>
    int Bitrate { get; set; }

    /// <returns>The length of the packet written into <paramref name="packet"/>.</returns>
    int Encode(ReadOnlySpan<short> pcm, int frameSamples, Span<byte> packet);
}

/// <summary>Turns one Opus packet back into 20 ms of interleaved float PCM.</summary>
public interface IAudioDecoder : IDisposable
{
    bool IsNative { get; }

    /// <param name="packet">Empty when the packet was lost: the decoder then conceals the gap.</param>
    void Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSamples);
}
