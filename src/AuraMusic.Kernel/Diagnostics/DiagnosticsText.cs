namespace AuraMusic.Kernel.Diagnostics;

/// <summary>One line of the diagnostics screen.</summary>
public readonly record struct DiagnosticsLine(string Label, string Value)
{
    public static implicit operator DiagnosticsLine((string Label, string Value) line) => new(line.Label, line.Value);
}

/// <summary>Turns a report into the label / value lines of the diagnostics screen.</summary>
public static class DiagnosticsText
{
    public static ImmutableArray<DiagnosticsLine> Lines(DiagnosticsReport report) => report switch
    {
        MasterReport master =>
        [
            ("codec", Codec(master.NativeCodec)),
            ("bitrate", $"{master.BitrateKbps} kbps"),
            ("encode", Invariant($"{master.EncodeMs:F1} ms / {PlayoutTuning.FrameMs} ms")),
            ("backlog", Invariant($"{master.EncodeBacklog}")),
            .. master.Links.Select(link => (
                $"{link.Listener} · {Describe(link.Kind)}",
                Invariant($"{link.FramesPerSecond:F0} fps, queue {link.Queued}") + (link.Carrying ? "" : " (idle)"))),
        ],
        ListenerReport listener =>
        [
            ("codec", Codec(listener.NativeCodec)),
            ("links", Describe(listener.Links)),
            ("bluetooth", Invariant($"{listener.BluetoothFramesPerSecond:F0} fps")),
            ("wifi", Invariant($"{listener.WifiFramesPerSecond:F0} fps")),
            ("bitrate", $"{listener.Kbps} kbps"),
            ("buffer", $"{listener.BufferedMs} / {listener.TargetMs} ms"),
            ("drift", Invariant($"{listener.DriftPpm:+0;-0;0} ppm")),
            ("caught up", Invariant($"{listener.CaughtUp}")),
            ("concealed", Invariant($"{listener.Concealed}")),
            ("skipped", Invariant($"{listener.Skipped}")),
            ("rebuffers", Invariant($"{listener.Rebuffers}")),
            ("dropped", Invariant($"{listener.Dropped}")),
        ],
        NoReport => [],
    };

    /// <summary>"Wi-Fi + Bluetooth", "Wi-Fi", "Bluetooth" or "-".</summary>
    public static string Describe(Links links) => links switch
    {
        Links.Wifi | Links.Bluetooth => "Wi-Fi + Bluetooth",
        Links.Wifi => "Wi-Fi",
        Links.Bluetooth => "Bluetooth",
        _ => "-",
    };

    static string Codec(bool native) => native ? "libopus" : "managed (fallback)";

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
