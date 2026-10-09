using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace AuraCast.Kernel.Localization;

/// <summary>Which language the app speaks: the one the user picked, else the phone's, else French.</summary>
public static class AppLanguages
{
    public const string Default = "fr";

    /// <summary>Order of the language button: each tap moves to the next one.</summary>
    public static readonly ImmutableArray<string> SwitchOrder = ["fr", "en"];

    public static readonly FrozenSet<string> Supported = SwitchOrder.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string Resolve(string? chosen, CultureInfo device)
    {
        if (chosen is not null && Supported.TryGetValue(chosen, out var language))
            return language;
        return Supported.TryGetValue(device.TwoLetterISOLanguageName, out var deviceLanguage) ? deviceLanguage : Default;
    }

    public static string Next(string current)
    {
        int index = SwitchOrder.IndexOf(Resolve(current, CultureInfo.InvariantCulture));
        return SwitchOrder[(index + 1) % SwitchOrder.Length];
    }
}
