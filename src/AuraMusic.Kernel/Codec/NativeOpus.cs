namespace AuraMusic.Kernel.Codec;

/// <summary>
/// The few libopus entry points we need. The library ships with the app (libopus.so on Android, opus.dll /
/// opus.so where the tests run); "opus" resolves to whichever is there.
/// </summary>
static partial class NativeOpus
{
    const string Library = "opus";

    public const int Ok = 0;
    public const int ApplicationAudio = 2049;
    public const int SetBitrate = 4002;
    public const int SetMaxBandwidth = 4004;
    public const int SetComplexity = 4010;
    public const int SetSignal = 4024;
    public const int SignalMusic = 3002;
    public const int BandwidthFullband = 1105;

    [LibraryImport(Library)]
    public static partial nint opus_encoder_create(int sampleRate, int channels, int application, out int error);

    [LibraryImport(Library)]
    public static partial void opus_encoder_destroy(nint encoder);

    [LibraryImport(Library)]
    public static partial int opus_encode(nint encoder, in short pcm, int frameSamples, out byte packet, int maxPacketBytes);

    // Variadic in C; for a single int argument every ABI we run on passes it like a regular one.
    [LibraryImport(Library)]
    public static partial int opus_encoder_ctl(nint encoder, int request, int value);

    [LibraryImport(Library)]
    public static partial nint opus_decoder_create(int sampleRate, int channels, out int error);

    [LibraryImport(Library)]
    public static partial void opus_decoder_destroy(nint decoder);

    /// <param name="packet">Null with a zero length asks the decoder to conceal a lost packet.</param>
    [LibraryImport(Library)]
    public static unsafe partial int opus_decode_float(nint decoder, byte* packet, int packetBytes, out float pcm, int frameSamples, int decodeFec);

    public static void ThrowIfFailed(int code, string operation)
    {
        if (code < Ok)
            throw new InvalidOperationException($"libopus {operation} failed ({code}).");
    }
}

sealed class NativeOpusEncoder : IAudioEncoder
{
    nint handle;

    public NativeOpusEncoder(int sampleRate, int channels, int bitrate, int complexity)
    {
        handle = NativeOpus.opus_encoder_create(sampleRate, channels, NativeOpus.ApplicationAudio, out int error);
        NativeOpus.ThrowIfFailed(error, "encoder_create");
        Set(NativeOpus.SetComplexity, complexity);
        Set(NativeOpus.SetSignal, NativeOpus.SignalMusic);
        Set(NativeOpus.SetMaxBandwidth, NativeOpus.BandwidthFullband); // never trade away the highs
        Bitrate = bitrate;
    }

    public bool IsNative => true;

    public int Bitrate
    {
        get;
        set
        {
            Set(NativeOpus.SetBitrate, value);
            field = value;
        }
    }

    public int Encode(ReadOnlySpan<short> pcm, int frameSamples, Span<byte> packet)
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
        int length = NativeOpus.opus_encode(handle, in pcm[0], frameSamples, out packet[0], packet.Length);
        NativeOpus.ThrowIfFailed(length, "encode");
        return length;
    }

    void Set(int request, int value) => NativeOpus.ThrowIfFailed(NativeOpus.opus_encoder_ctl(handle, request, value), $"ctl {request}");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref handle, 0) is var encoder and not 0)
            NativeOpus.opus_encoder_destroy(encoder);
        GC.SuppressFinalize(this);
    }

    ~NativeOpusEncoder() => Dispose();
}

sealed class NativeOpusDecoder : IAudioDecoder
{
    nint handle;

    public NativeOpusDecoder(int sampleRate, int channels)
    {
        handle = NativeOpus.opus_decoder_create(sampleRate, channels, out int error);
        NativeOpus.ThrowIfFailed(error, "decoder_create");
    }

    public bool IsNative => true;

    public unsafe void Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSamples)
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
        fixed (byte* data = packet) // null for an empty span: that is how libopus is told to conceal
        {
            int decoded = NativeOpus.opus_decode_float(handle, packet.IsEmpty ? null : data, packet.Length, out pcm[0], frameSamples, decodeFec: 0);
            NativeOpus.ThrowIfFailed(decoded, "decode");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref handle, 0) is var decoder and not 0)
            NativeOpus.opus_decoder_destroy(decoder);
        GC.SuppressFinalize(this);
    }

    ~NativeOpusDecoder() => Dispose();
}
