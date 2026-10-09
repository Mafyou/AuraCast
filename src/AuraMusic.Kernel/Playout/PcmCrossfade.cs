namespace AuraMusic.Kernel.Playout;

/// <summary>Click-free edits on interleaved PCM frames.</summary>
public static class PcmCrossfade
{
    /// <summary>
    /// Removes <paramref name="framesToRemove"/> sample frames from the middle of <paramref name="pcm"/>, crossfading
    /// over <paramref name="fadeFrames"/> so the splice cannot be heard. The result is moved to the start of the span.
    /// </summary>
    /// <returns>How many interleaved samples of <paramref name="pcm"/> now hold the shortened audio.</returns>
    public static int Shorten(Span<float> pcm, int channels, int framesToRemove, int fadeFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegative(framesToRemove);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fadeFrames);
        int frames = pcm.Length / channels;
        if (framesToRemove + fadeFrames > frames)
            throw new ArgumentOutOfRangeException(nameof(framesToRemove), "Cannot remove more than the frame holds.");
        if (framesToRemove == 0)
            return pcm.Length;

        // Keep [0, start), blend [start, start + fade) with the audio that follows the removed part, keep the rest.
        int start = (frames - framesToRemove - fadeFrames) / 2;
        for (int i = 0; i < fadeFrames; i++)
        {
            float weight = (i + 0.5f) / fadeFrames;
            for (int channel = 0; channel < channels; channel++)
            {
                int kept = (start + i) * channels + channel;
                int incoming = (start + framesToRemove + i) * channels + channel;
                pcm[kept] = pcm[kept] * (1 - weight) + pcm[incoming] * weight;
            }
        }
        pcm[((start + framesToRemove + fadeFrames) * channels)..].CopyTo(pcm[((start + fadeFrames) * channels)..]);
        return pcm.Length - framesToRemove * channels;
    }
}
