namespace AuraMusic.Kernel.State;

public sealed record Idle;
public sealed record Advertising;

/// <param name="Listeners">The names of the connected listeners' phones, as Bluetooth reports them.</param>
public sealed record Streaming(ImmutableArray<string> Listeners)
{
    public int Count => Listeners.Length;
}

public sealed record Searching;
public sealed record Listening(string Master);
public sealed record Failed(string Reason);

public union AuraState(Idle, Advertising, Streaming, Searching, Listening, Failed);

/// <summary>Single source of truth for the streaming state, shared between the services and the UI.</summary>
public static class AuraHub
{
    public static event Action<AuraState>? StateChanged;

    public static AuraState Current
    {
        get;
        private set
        {
            field = value;
            StateChanged?.Invoke(value);
        }
    } = new Idle();

    public static void Publish(AuraState state) => Current = state;
}
