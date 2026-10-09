namespace AuraMusic.Kernel.Tests.Spectrum;

public sealed class SpectrumAnalyzerTests
{
    const int SampleRate = 48_000;
    const int Frames = 960;

    static float[] StereoSine(float frequency, float amplitude) =>
        [.. Enumerable.Range(0, Frames).SelectMany(i =>
        {
            float sample = amplitude * MathF.Sin(2 * MathF.PI * frequency * i / SampleRate);
            return new[] { sample, sample };
        })];

    static float[] Analyze(float[] pcm)
    {
        var analyzer = new SpectrumAnalyzer(16, SampleRate, channels: 2);
        var levels = new float[analyzer.Bands];
        analyzer.Analyze(pcm, levels);
        return levels;
    }

    [Fact]
    public void Silence_ReadsZeroEverywhere()
    {
        Analyze(new float[Frames * 2]).ShouldAllBe(level => level == 0);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(8_000)]
    public void FullScaleTone_LightsItsBandNearTheTop(float frequency)
    {
        var levels = Analyze(StereoSine(frequency, 1f));

        levels.Max().ShouldBeGreaterThan(0.9f);
    }

    [Fact]
    public void Tone_LightsItsOwnBandNotTheFarOnes()
    {
        var levels = Analyze(StereoSine(1_000, 1f));
        int loudest = Array.IndexOf(levels, levels.Max());

        levels[0].ShouldBeLessThan(levels[loudest] - 0.3f);
        levels[^1].ShouldBeLessThan(levels[loudest] - 0.3f);
    }

    [Fact]
    public void HigherTone_LightsAHigherBand()
    {
        var low = Analyze(StereoSine(200, 1f));
        var high = Analyze(StereoSine(5_000, 1f));

        Array.IndexOf(high, high.Max()).ShouldBeGreaterThan(Array.IndexOf(low, low.Max()));
    }

    [Fact]
    public void QuieterTone_ReadsLower()
    {
        var loud = Analyze(StereoSine(1_000, 1f));
        var quiet = Analyze(StereoSine(1_000, 0.05f));

        quiet.Max().ShouldBeLessThan(loud.Max());
    }

    [Fact]
    public void ShortSamples_MatchFloatSamples()
    {
        var floats = StereoSine(1_000, 0.5f);
        short[] shorts = [.. floats.Select(sample => (short)(sample * 32767))];
        var analyzer = new SpectrumAnalyzer(16, SampleRate, channels: 2);
        var fromFloats = new float[16];
        var fromShorts = new float[16];

        analyzer.Analyze(floats, fromFloats);
        analyzer.Analyze(shorts, fromShorts);

        for (int band = 0; band < 16; band++)
            fromShorts[band].ShouldBe(fromFloats[band], tolerance: 0.01f);
    }
}
