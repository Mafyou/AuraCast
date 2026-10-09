namespace AuraMusic.Kernel.Spectrum;

/// <summary>
/// Turns a frame of interleaved PCM into a handful of log-spaced band levels (0 = silence, 1 = full scale),
/// for the visualizer. Not thread-safe: one instance per audio thread.
/// </summary>
public sealed class SpectrumAnalyzer
{
    const int FftSize = 1024;
    const float MinFrequency = 60;
    const float MaxFrequency = 16_000;
    const float FloorDecibels = -60;

    readonly int channels;
    readonly (int First, int Last)[] bandBins;
    readonly float[] real = new float[FftSize];
    readonly float[] imaginary = new float[FftSize];
    float[] window = [];
    float windowGain;

    public SpectrumAnalyzer(int bands, int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = channels;
        bandBins = new (int, int)[bands];
        float binWidth = (float)sampleRate / FftSize;
        for (int band = 0; band < bands; band++)
        {
            float low = MinFrequency * MathF.Pow(MaxFrequency / MinFrequency, (float)band / bands);
            float high = MinFrequency * MathF.Pow(MaxFrequency / MinFrequency, (float)(band + 1) / bands);
            int first = Math.Clamp((int)(low / binWidth), 1, FftSize / 2 - 1);
            int last = Math.Clamp((int)(high / binWidth), first, FftSize / 2 - 1);
            bandBins[band] = (first, last);
        }
    }

    public int Bands => bandBins.Length;

    public void Analyze(ReadOnlySpan<float> pcm, Span<float> levels)
    {
        int frames = Math.Min(pcm.Length / channels, FftSize);
        EnsureWindow(frames);
        for (int i = 0; i < frames; i++)
        {
            float mono = 0;
            for (int channel = 0; channel < channels; channel++)
                mono += pcm[i * channels + channel];
            real[i] = mono / channels * window[i];
        }
        Transform(frames, levels);
    }

    public void Analyze(ReadOnlySpan<short> pcm, Span<float> levels)
    {
        int frames = Math.Min(pcm.Length / channels, FftSize);
        EnsureWindow(frames);
        for (int i = 0; i < frames; i++)
        {
            float mono = 0;
            for (int channel = 0; channel < channels; channel++)
                mono += pcm[i * channels + channel];
            real[i] = mono / (channels * 32768f) * window[i];
        }
        Transform(frames, levels);
    }

    void EnsureWindow(int frames)
    {
        if (window.Length == frames)
            return;
        // Hann window: keeps a pure tone from leaking into every band.
        window = [.. Enumerable.Range(0, frames).Select(i => 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / Math.Max(1, frames - 1)))];
        windowGain = window.Sum();
    }

    void Transform(int frames, Span<float> levels)
    {
        real.AsSpan(frames).Clear(); // zero-padding up to the FFT size
        imaginary.AsSpan().Clear();
        Fft(real, imaginary);

        for (int band = 0; band < bandBins.Length && band < levels.Length; band++)
        {
            var (first, last) = bandBins[band];
            float peak = 0;
            for (int bin = first; bin <= last; bin++)
                peak = MathF.Max(peak, MathF.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]));
            // A full-scale sine reads 1 (0 dB); the display covers 60 dB below that.
            float amplitude = 2 * peak / windowGain;
            float decibels = 20 * MathF.Log10(amplitude + 1e-9f);
            levels[band] = Math.Clamp((decibels - FloorDecibels) / -FloorDecibels, 0, 1);
        }
    }

    /// <summary>In-place iterative radix-2 FFT.</summary>
    static void Fft(Span<float> re, Span<float> im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            float angle = -2 * MathF.PI / length;
            float stepRe = MathF.Cos(angle), stepIm = MathF.Sin(angle);
            for (int start = 0; start < n; start += length)
            {
                float wRe = 1, wIm = 0;
                for (int k = 0; k < length / 2; k++)
                {
                    int a = start + k, b = a + length / 2;
                    float tRe = re[b] * wRe - im[b] * wIm;
                    float tIm = re[b] * wIm + im[b] * wRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    (wRe, wIm) = (wRe * stepRe - wIm * stepIm, wRe * stepIm + wIm * stepRe);
                }
            }
        }
    }
}
