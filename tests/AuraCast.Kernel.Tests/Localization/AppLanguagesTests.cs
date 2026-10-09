using System.Globalization;
using AuraCast.Kernel.Localization;
using Shouldly;

namespace AuraCast.Kernel.Tests.Localization;

public sealed class AppLanguagesTests
{
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("fr", "fr")]
    public void Resolve_ChosenLanguage_WinsOverThePhone(string chosen, string expected)
    {
        AppLanguages.Resolve(chosen, German).ShouldBe(expected);
    }

    [Fact]
    public void Resolve_NothingChosen_FollowsAnEnglishPhone()
    {
        AppLanguages.Resolve(null, English).ShouldBe("en");
    }

    [Fact]
    public void Resolve_NothingChosen_FollowsAFrenchPhone()
    {
        AppLanguages.Resolve(null, French).ShouldBe("fr");
    }

    [Fact]
    public void Resolve_UnsupportedPhoneLanguage_FallsBackToFrench()
    {
        AppLanguages.Resolve(null, German).ShouldBe(AppLanguages.Default);
    }

    [Fact]
    public void Resolve_UnknownChoice_IsIgnored()
    {
        AppLanguages.Resolve("klingon", English).ShouldBe("en");
    }

    [Theory]
    [InlineData("fr", "en")]
    [InlineData("en", "fr")]
    [InlineData("EN", "fr")]
    public void Next_CyclesThroughTheLanguages(string current, string expected)
    {
        AppLanguages.Next(current).ShouldBe(expected);
    }

    [Fact]
    public void SwitchOrder_CoversExactlyTheSupportedLanguages()
    {
        AppLanguages.SwitchOrder.ShouldBe(AppLanguages.Supported, ignoreOrder: true);
    }
}
