namespace AuraMusic.Kernel.Tests.Playout;

public sealed class PcmCrossfadeTests
{
    const int Channels = 2;
    const int Frames = 960;

    static float[] Stereo(Func<int, float> left, Func<int, float> right)
    {
        var pcm = new float[Frames * Channels];
        for (int i = 0; i < Frames; i++)
        {
            pcm[i * 2] = left(i);
            pcm[i * 2 + 1] = right(i);
        }
        return pcm;
    }

    [Fact]
    public void Shorten_RemovesExactlyTheRequestedFrames()
    {
        var pcm = Stereo(_ => 0.5f, _ => -0.5f);

        PcmCrossfade.Shorten(pcm, Channels, framesToRemove: 48, fadeFrames: 96).ShouldBe((Frames - 48) * Channels);
    }

    [Fact]
    public void Shorten_ConstantSignal_StaysConstant()
    {
        var pcm = Stereo(_ => 0.5f, _ => -0.25f);

        int length = PcmCrossfade.Shorten(pcm, Channels, 48, 96);

        for (int i = 0; i < length; i += Channels)
        {
            pcm[i].ShouldBe(0.5f, tolerance: 1e-6f);
            pcm[i + 1].ShouldBe(-0.25f, tolerance: 1e-6f);
        }
    }

    [Fact]
    public void Shorten_KeepsChannelsApart()
    {
        var pcm = Stereo(_ => 1f, _ => 0f);

        int length = PcmCrossfade.Shorten(pcm, Channels, 48, 96);

        for (int i = 0; i < length; i += Channels)
        {
            pcm[i].ShouldBe(1f, tolerance: 1e-6f);
            pcm[i + 1].ShouldBe(0f, tolerance: 1e-6f);
        }
    }

    [Fact]
    public void Shorten_SineWave_HasNoClick()
    {
        // A 440 Hz tone: neighbouring samples never differ by more than ~0.06 of full scale.
        var pcm = Stereo(i => MathF.Sin(2 * MathF.PI * 440 * i / 48_000f), i => MathF.Sin(2 * MathF.PI * 440 * i / 48_000f));

        int length = PcmCrossfade.Shorten(pcm, Channels, 48, 96);

        for (int i = Channels; i < length; i += Channels)
            MathF.Abs(pcm[i] - pcm[i - Channels]).ShouldBeLessThan(0.12f);
    }

    [Fact]
    public void Shorten_KeepsTheEdgesUntouched()
    {
        var pcm = Stereo(i => i, i => -i);
        var original = (float[])pcm.Clone();

        int length = PcmCrossfade.Shorten(pcm, Channels, 48, 96);

        pcm[..20].ShouldBe(original[..20]);
        pcm[(length - 20)..length].ShouldBe(original[^20..]);
    }

    [Fact]
    public void Shorten_NothingToRemove_LeavesTheFrame()
    {
        var pcm = Stereo(i => i, i => i);
        var original = (float[])pcm.Clone();

        PcmCrossfade.Shorten(pcm, Channels, 0, 96).ShouldBe(pcm.Length);
        pcm.ShouldBe(original);
    }

    [Fact]
    public void Shorten_MoreThanTheFrame_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PcmCrossfade.Shorten(new float[200], Channels, 90, 20));
    }
}
