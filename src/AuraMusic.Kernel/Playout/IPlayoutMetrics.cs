namespace AuraMusic.Kernel.Playout;

/// <summary>Receives what the playout did besides playing packets normally.</summary>
public interface IPlayoutMetrics
{
    void CaughtUp();

    void Concealed();

    void Skipped();

    void Rebuffered();
}
