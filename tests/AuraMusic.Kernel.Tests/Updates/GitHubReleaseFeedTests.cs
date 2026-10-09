namespace AuraMusic.Kernel.Tests.Updates;

public sealed class GitHubReleaseFeedTests
{
    const string Release = """
        {
          "tag_name": "v1.3.0",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt" },
            { "name": "AuraMusic-v1.3.0.apk", "browser_download_url": "https://github.com/Mafyou/AuraMusic/releases/download/v1.3.0/AuraMusic-v1.3.0.apk" }
          ]
        }
        """;

    [Fact]
    public void Parse_FindsTheVersionAndTheApk()
    {
        var release = GitHubReleaseFeed.Parse(Release).ShouldNotBeNull();

        release.Version.ShouldBe(new Version(1, 3, 0));
        release.Tag.ShouldBe("v1.3.0");
        release.ApkUrl.AbsoluteUri.ShouldEndWith("AuraMusic-v1.3.0.apk");
    }

    [Fact]
    public void Parse_ReleaseWithoutApk_IsIgnored()
    {
        GitHubReleaseFeed.Parse("""{ "tag_name": "v1.3.0", "assets": [] }""").ShouldBeNull();
    }

    [Fact]
    public void Parse_TagThatIsNotAVersion_IsIgnored()
    {
        GitHubReleaseFeed.Parse(Release.Replace("v1.3.0\",", "nightly\",")).ShouldBeNull();
    }

    [Fact]
    public async Task GetLatestAsync_AsksGitHubWithAUserAgent()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Release);
        var feed = new GitHubReleaseFeed(new HttpClient(handler), "Mafyou", "AuraMusic");

        var release = await feed.GetLatestAsync(TestContext.Current.CancellationToken);

        release.ShouldNotBeNull().Version.ShouldBe(new Version(1, 3, 0));
        handler.Request.ShouldNotBeNull().RequestUri!.AbsoluteUri.ShouldBe("https://api.github.com/repos/Mafyou/AuraMusic/releases/latest");
        handler.Request.Headers.UserAgent.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_NoReleaseYet_ReturnsNull()
    {
        var feed = new GitHubReleaseFeed(new HttpClient(new FakeHandler(HttpStatusCode.NotFound, "")), "Mafyou", "AuraMusic");

        (await feed.GetLatestAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task GetLatestAsync_ServerError_Throws()
    {
        var feed = new GitHubReleaseFeed(new HttpClient(new FakeHandler(HttpStatusCode.InternalServerError, "")), "Mafyou", "AuraMusic");

        await Should.ThrowAsync<HttpRequestException>(() => feed.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
