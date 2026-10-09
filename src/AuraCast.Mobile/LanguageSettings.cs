using System.Globalization;
using AuraCast.Kernel.Localization;

namespace AuraCast.Mobile;

/// <summary>Applies the language the user picked (or the phone's) to every resource lookup.</summary>
static class LanguageSettings
{
    const string PreferenceKey = "language";

    // Captured before we override it, so "follow the phone" keeps meaning the phone.
    static readonly CultureInfo DeviceCulture = CultureInfo.CurrentUICulture;

    public static string Current { get; private set; } = AppLanguages.Default;

    public static void Apply() => Apply(AppLanguages.Resolve(Preferences.Get(PreferenceKey, null), DeviceCulture));

    public static void SwitchToNext()
    {
        var next = AppLanguages.Next(Current);
        Preferences.Set(PreferenceKey, next);
        Apply(next);
    }

    static void Apply(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture; // background services too
        CultureInfo.CurrentUICulture = culture;
        Current = language;
    }
}
