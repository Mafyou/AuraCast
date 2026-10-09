namespace AuraMusic.Kernel.Spectrum;

/// <summary>Carries band levels from whichever audio thread is running (capture or playback) to the visualizer.</summary>
public static class SpectrumHub
{
    public const int Bands = 16;

    public static event Action<float[]>? Updated;

    /// <param name="levels">A fresh array each time: subscribers may keep it.</param>
    public static void Publish(float[] levels) => Updated?.Invoke(levels);
}
