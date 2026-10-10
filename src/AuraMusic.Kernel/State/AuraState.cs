namespace AuraMusic.Kernel.State;

public sealed record Idle;
public sealed record Advertising;

/// <param name="Listeners">The names of the connected listeners' phones (Bluetooth name, or the one sent over Wi-Fi).</param>
public sealed record Streaming(ImmutableArray<string> Listeners)
{
    public int Count => Listeners.Length;
}

/// <param name="Problem">Why nothing is found yet (Bluetooth off…), shown instead of the generic hint.</param>
public sealed record Searching(string? Problem = null);

/// <summary>The links a listener currently receives the stream over.</summary>
[Flags]
public enum Links
{
    None = 0,
    Bluetooth = 1,
    Wifi = 2,
}

public sealed record Listening(string Master, Links Links);
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
