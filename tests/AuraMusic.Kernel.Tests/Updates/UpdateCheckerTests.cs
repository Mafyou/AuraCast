namespace AuraMusic.Kernel.Tests.Updates;

public sealed class UpdateCheckerTests
{
    static readonly AppRelease V130 = new(new Version(1, 3, 0), "v1.3.0", new Uri("https://example.com/AuraMusic-v1.3.0.apk"));

    readonly Mock<IReleaseFeed> feed = new();

    Task<UpdateCheck> Check(string installed) =>
        new UpdateChecker(feed.Object).CheckAsync(installed, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.2")]
    [InlineData("0.9")]
    public async Task NewerRelease_IsOffered(string installed)
    {
        feed.Setup(f => f.GetLatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(V130);

        var check = await Check(installed);

        var available = check is UpdateAvailable a ? a : null;
        available.ShouldNotBeNull().Release.ShouldBe(V130);
    }

    [Theory]
    [InlineData("1.3.0")]
    [InlineData("1.3")]
    [InlineData("2.0.0")]
    public async Task SameOrOlderRelease_IsUpToDate(string installed)
    {
        feed.Setup(f => f.GetLatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(V130);

        (await Check(installed) is UpToDate).ShouldBeTrue();
    }

    [Fact]
    public async Task NoRelease_IsUpToDate()
    {
        feed.Setup(f => f.GetLatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AppRelease?)null);

        (await Check("1.0") is UpToDate).ShouldBeTrue();
    }

    [Fact]
    public async Task UnreadableInstalledVersion_IsNeverNagged()
    {
        feed.Setup(f => f.GetLatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(V130);

        (await Check("dev") is UpToDate).ShouldBeTrue();
    }

    [Fact]
    public async Task Offline_ReportsTheFailureInsteadOfThrowing()
    {
        feed.Setup(f => f.GetLatestAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("no network"));

        var check = await Check("1.2.0");

        var failed = check is CheckFailed f ? f : null;
        failed.ShouldNotBeNull().Reason.ShouldBe("no network");
    }

    [Fact]
    public async Task StoppingToken_IsPassedToTheFeed()
    {
        using var stopping = new CancellationTokenSource();
        feed.Setup(f => f.GetLatestAsync(stopping.Token)).ReturnsAsync(V130);

        await new UpdateChecker(feed.Object).CheckAsync("1.2.0", stopping.Token);

        feed.Verify(f => f.GetLatestAsync(stopping.Token), Times.Once);
    }
}
