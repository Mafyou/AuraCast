namespace AuraMusic.Kernel.Multipath;

/// <summary>
/// The phones allowed to listen over Wi-Fi: those that already connected over Bluetooth, which requires pairing.
/// Anyone can reach a TCP port on a shared network; nobody unpaired gets the stream.
/// </summary>
public sealed class TrustedDevices
{
    readonly ImmutableHashSet<Guid> ids;

    TrustedDevices(ImmutableHashSet<Guid> ids) => this.ids = ids;

    public static TrustedDevices Empty { get; } = new([]);

    public int Count => ids.Count;

    public bool Contains(Guid id) => ids.Contains(id);

    /// <returns>This instance when <paramref name="id"/> was already trusted.</returns>
    public TrustedDevices With(Guid id) => ids.Contains(id) ? this : new(ids.Add(id));

    /// <summary>For the app's preferences; unreadable entries are skipped.</summary>
    public static TrustedDevices Parse(string? stored) => new(
    [
        .. (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => Guid.TryParse(text, out var id) ? id : (Guid?)null)
            .OfType<Guid>(),
    ]);

    public string Serialize() => string.Join(',', ids.Order().Select(id => id.ToString("N")));
}
