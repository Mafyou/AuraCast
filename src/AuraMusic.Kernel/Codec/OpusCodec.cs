namespace AuraMusic.Kernel.Codec;

/// <summary>
/// Opus for the stream: libopus when it loads (several times faster, so the master's phone stays cool and
/// can afford full complexity), the managed implementation otherwise. Both speak the same bitstream.
/// </summary>
public static class OpusCodec
{
    // Native code is cheap enough for libopus's best effort; the managed encoder needs to stay light.
    const int NativeComplexity = 10;
    const int ManagedComplexity = 5;

    // Our own bridge handles libopus; Concentus is only ever the pure C# fallback.
    static OpusCodec() => Concentus.OpusCodecFactory.AttemptToUseNativeLibrary = false;

    public static IAudioEncoder CreateEncoder(int sampleRate, int channels, int bitrate)
    {
        try
        {
            return new NativeOpusEncoder(sampleRate, channels, bitrate, NativeComplexity);
        }
        catch (Exception ex) when (IsMissingLibrary(ex))
        {
            return new ManagedOpusEncoder(sampleRate, channels, bitrate, ManagedComplexity);
        }
    }

    public static IAudioDecoder CreateDecoder(int sampleRate, int channels)
    {
        try
        {
            return new NativeOpusDecoder(sampleRate, channels);
        }
        catch (Exception ex) when (IsMissingLibrary(ex))
        {
            return new ManagedOpusDecoder(sampleRate, channels);
        }
    }

    /// <summary>For tests and comparisons: the pure C# codec, whatever the machine has.</summary>
    public static IAudioEncoder CreateManagedEncoder(int sampleRate, int channels, int bitrate) =>
        new ManagedOpusEncoder(sampleRate, channels, bitrate, ManagedComplexity);

    /// <inheritdoc cref="CreateManagedEncoder"/>
    public static IAudioDecoder CreateManagedDecoder(int sampleRate, int channels) => new ManagedOpusDecoder(sampleRate, channels);

    static bool IsMissingLibrary(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    sealed class ManagedOpusEncoder : IAudioEncoder
    {
        readonly Concentus.IOpusEncoder encoder;

        public ManagedOpusEncoder(int sampleRate, int channels, int bitrate, int complexity)
        {
            encoder = Concentus.OpusCodecFactory.CreateEncoder(sampleRate, channels, Concentus.Enums.OpusApplication.OPUS_APPLICATION_AUDIO);
            encoder.Complexity = complexity;
            encoder.SignalType = Concentus.Enums.OpusSignal.OPUS_SIGNAL_MUSIC;
            encoder.MaxBandwidth = Concentus.Enums.OpusBandwidth.OPUS_BANDWIDTH_FULLBAND;
            encoder.Bitrate = bitrate;
        }

        public bool IsNative => false;

        public int Bitrate
        {
            get => encoder.Bitrate;
            set => encoder.Bitrate = value;
        }

        public int Encode(ReadOnlySpan<short> pcm, int frameSamples, Span<byte> packet) =>
            encoder.Encode(pcm, frameSamples, packet, packet.Length);

        public void Dispose() => encoder.Dispose();
    }

    sealed class ManagedOpusDecoder(int sampleRate, int channels) : IAudioDecoder
    {
        readonly Concentus.IOpusDecoder decoder = Concentus.OpusCodecFactory.CreateDecoder(sampleRate, channels);

        public bool IsNative => false;

        public void Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSamples) => decoder.Decode(packet, pcm, frameSamples);

        public void Dispose() => decoder.Dispose();
    }
}
