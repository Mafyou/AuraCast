namespace AuraCast.Mobile.Core;

public sealed record Idle;
public sealed record Advertising;
public sealed record Streaming(int Listeners);
public sealed record Searching;
public sealed record Listening(string Master);
public sealed record Failed(string Reason);

public union AuraState(Idle, Advertising, Streaming, Searching, Listening, Failed);

/// <summary>Single source of truth for the streaming state, shared between the services and the UI.</summary>
public static class AuraHub
{
    public static AuraState Current { get; private set; } = new Idle();

    public static event Action<AuraState>? StateChanged;

    public static void Publish(AuraState state)
    {
        Current = state;
        StateChanged?.Invoke(state);
    }
}
