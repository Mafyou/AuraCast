namespace AuraMusic.Kernel.Playout;

/// <summary>
/// Two phones never run at exactly the same sample rate: left alone, the listener's buffer slowly fills up or
/// runs dry. This keeps it at its target by playing a hair faster or slower (a few hundredths of a percent,
/// far below what an ear can tell), instead of shortening or concealing packets now and then.
/// </summary>
public sealed class DriftController(int nominalRate)
{
    /// <summary>The correction never exceeds ±0.3 %: about a twentieth of a semitone.</summary>
    public const double MaxCorrection = 0.003;

    // The buffer level jumps by a packet each time one arrives or is played: follow its trend (≈1 s), not that.
    const double Smoothing = 0.02;
    // 100 ms away from the target is corrected at 0.2 %.
    const double ProportionalPerMs = 2e-5;
    // Learns the steady clock difference, so the buffer ends up on the target rather than next to it.
    const double IntegralPerMs = 2e-8;
    const double MaxIntegral = 0.001;

    double integral;
    bool primed;

    /// <summary>Buffered audio to aim for, in milliseconds.</summary>
    public double TargetMs { get; set; } = PlayoutTuning.DefaultLatencyMs;

    /// <summary>The smoothed buffer level the correction is based on.</summary>
    public double SmoothedMs { get; private set; }

    /// <summary>Current speed correction: 0.0001 means playing 100 ppm faster than nominal.</summary>
    public double Correction { get; private set; }

    /// <summary>Call once per played packet.</summary>
    /// <param name="bufferedMs">Audio currently waiting, jitter buffer and audio output together.</param>
    /// <returns>The sample rate to play at.</returns>
    public int Update(double bufferedMs)
    {
        if (!primed)
        {
            SmoothedMs = bufferedMs;
            primed = true;
        }
        SmoothedMs += (bufferedMs - SmoothedMs) * Smoothing;

        double error = SmoothedMs - TargetMs; // too much waiting: play faster
        integral = Math.Clamp(integral + error * IntegralPerMs, -MaxIntegral, MaxIntegral);
        Correction = Math.Clamp(error * ProportionalPerMs + integral, -MaxCorrection, MaxCorrection);
        return (int)Math.Round(nominalRate * (1 + Correction));
    }

    /// <summary>After a rebuffer the level starts afresh; what was learnt about the clocks is kept.</summary>
    public void Restart() => primed = false;

    /// <summary>
    /// The buffer ran dry while playing: whatever said "play faster" was wrong. Forget it, so a level that reads
    /// too high (or a target set too low) cannot keep draining the buffer.
    /// </summary>
    public void RanDry()
    {
        integral = Math.Min(integral, 0);
        Correction = Math.Min(Correction, 0);
        primed = false;
    }
}
