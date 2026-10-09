namespace AuraMusic.Kernel.Tests.Updates;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("V2.0.1", 2, 0, 1)]
    [InlineData("1.0", 1, 0, 0)]
    [InlineData(" 1.3 ", 1, 3, 0)]
    public void TryParse_ReadsThreeParts(string text, int major, int minor, int build)
    {
        AppVersion.TryParse(text, out var version).ShouldBeTrue();
        version.ShouldBe(new Version(major, minor, build));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void TryParse_RejectsNonVersions(string? text)
    {
        AppVersion.TryParse(text, out var version).ShouldBeFalse();
        version.ShouldBeNull();
    }

    [Fact]
    public void TwoAndThreePartVersions_CompareEqual()
    {
        AppVersion.TryParse("1.2", out var shortForm);
        AppVersion.TryParse("v1.2.0", out var tag);

        shortForm.ShouldBe(tag);
    }
}
