namespace AuraMusic.Kernel.Tests.Codec;

public sealed class OpusCodecTests
{
    const int SampleRate = AuraProtocol.SampleRate;
    const int Channels = AuraProtocol.Channels;
    const int FrameSamples = AuraProtocol.FrameSamples;
    const int Frames = 25; // half a second: enough for the codec to settle

    /// <summary>A 440 Hz stereo tone at half scale, <paramref name="frame"/> frames into the signal.</summary>
    static short[] Tone(int frame) =>
    [
        .. Enumerable.Range(frame * FrameSamples, FrameSamples).SelectMany(i =>
        {
            short sample = (short)(16_000 * MathF.Sin(2 * MathF.PI * 440 * i / SampleRate));
            return new[] { sample, sample };
        }),
    ];

    /// <returns>The RMS level of the last decoded frame, where 1 is full scale.</returns>
    static float RoundTrip(IAudioEncoder encoder, IAudioDecoder decoder)
    {
        var packet = new byte[AuraProtocol.MaxPacketSize];
        var pcm = new float[FrameSamples * Channels];
        for (int frame = 0; frame < Frames; frame++)
        {
            int length = encoder.Encode(Tone(frame), FrameSamples, packet);
            length.ShouldBeInRange(1, AuraProtocol.MaxPacketSize);
            decoder.Decode(packet.AsSpan(0, length), pcm, FrameSamples);
        }
        return MathF.Sqrt(pcm.Sum(sample => sample * sample) / pcm.Length);
    }

    // A half-scale sine has an RMS of 0.5 / sqrt(2).
    const float ExpectedLevel = 16_000 / 32_768f * 0.7071f;

    [Fact]
    public void Native_IsWhatTheFactoryPicks()
    {
        using var encoder = OpusCodec.CreateEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        using var decoder = OpusCodec.CreateDecoder(SampleRate, Channels);

        encoder.IsNative.ShouldBeTrue("libopus ships with the tests (OpusSharp.Natives)");
        decoder.IsNative.ShouldBeTrue();
    }

    [Fact]
    public void Native_RoundTripsTheSound()
    {
        using var encoder = OpusCodec.CreateEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        using var decoder = OpusCodec.CreateDecoder(SampleRate, Channels);

        RoundTrip(encoder, decoder).ShouldBe(ExpectedLevel, tolerance: 0.05f);
    }

    [Fact]
    public void Managed_RoundTripsTheSound()
    {
        using var encoder = OpusCodec.CreateManagedEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        using var decoder = OpusCodec.CreateManagedDecoder(SampleRate, Channels);

        encoder.IsNative.ShouldBeFalse();
        RoundTrip(encoder, decoder).ShouldBe(ExpectedLevel, tolerance: 0.05f);
    }

    [Fact]
    public void NativeMaster_IsUnderstoodByAManagedListener()
    {
        using var encoder = OpusCodec.CreateEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        using var decoder = OpusCodec.CreateManagedDecoder(SampleRate, Channels);

        RoundTrip(encoder, decoder).ShouldBe(ExpectedLevel, tolerance: 0.05f);
    }

    [Fact]
    public void ManagedMaster_IsUnderstoodByANativeListener()
    {
        using var encoder = OpusCodec.CreateManagedEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        using var decoder = OpusCodec.CreateDecoder(SampleRate, Channels);

        RoundTrip(encoder, decoder).ShouldBe(ExpectedLevel, tolerance: 0.05f);
    }

    [Fact]
    public void HigherBitrate_MakesBiggerPackets()
    {
        using var encoder = OpusCodec.CreateEncoder(SampleRate, Channels, 32_000);
        var packet = new byte[AuraProtocol.MaxPacketSize];
        // Noise: unlike a pure tone, it uses every bit the encoder is given.
        var random = new Random(7);
        short[] Noise() => [.. Enumerable.Range(0, FrameSamples * Channels).Select(_ => (short)random.Next(-12_000, 12_000))];

        int low = Enumerable.Range(0, Frames).Sum(_ => encoder.Encode(Noise(), FrameSamples, packet));
        encoder.Bitrate = AuraProtocol.WifiBitrate;
        int high = Enumerable.Range(0, Frames).Sum(_ => encoder.Encode(Noise(), FrameSamples, packet));

        encoder.Bitrate.ShouldBe(AuraProtocol.WifiBitrate);
        high.ShouldBeGreaterThan(low * 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LostPacket_IsConcealedNotThrown(bool native)
    {
        using var encoder = OpusCodec.CreateEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        using var decoder = native ? OpusCodec.CreateDecoder(SampleRate, Channels) : OpusCodec.CreateManagedDecoder(SampleRate, Channels);
        RoundTrip(encoder, decoder);
        var pcm = new float[FrameSamples * Channels];

        Should.NotThrow(() => decoder.Decode([], pcm, FrameSamples));

        pcm.ShouldContain(sample => sample != 0); // the tone carries on through the gap
    }

    [Fact]
    public void Disposed_Encoder_Throws()
    {
        var encoder = OpusCodec.CreateEncoder(SampleRate, Channels, AuraProtocol.Bitrate);
        encoder.Dispose();
        encoder.Dispose(); // harmless twice

        Should.Throw<ObjectDisposedException>(() => encoder.Encode(Tone(0), FrameSamples, new byte[AuraProtocol.MaxPacketSize]));
    }
}
