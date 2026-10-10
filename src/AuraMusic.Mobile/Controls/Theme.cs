namespace AuraMusic.Mobile.Controls;

/// <summary>Code-side access to Colors.xaml and Styles.xaml, so no colour or style is written in C#.</summary>
static class Theme
{
    public static Color Color(string key) => Resource<Color>(key);

    public static Style Style(string key) => Resource<Style>(key);

    static T Resource<T>(string key) => (T)Application.Current!.Resources[key];
}
