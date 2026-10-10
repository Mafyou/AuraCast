namespace AuraMusic.Kernel.Tests.Diagnostics;

public sealed class DiagnosticsTextTests
{
    static string Value(ImmutableArray<DiagnosticsLine> lines, string label) =>
        lines.Single(line => line.Label == label).Value;

    [Fact]
    public void Idle_HasNothingToShow()
    {
        DiagnosticsText.Lines(new NoReport()).ShouldBeEmpty();
    }

    [Fact]
    public void Master_ListsItsCodecAndEachLink()
    {
        var lines = DiagnosticsText.Lines(new MasterReport(true, 160, 2.14, 0,
        [
            new LinkReport("Nord 2", Links.Wifi, 50.2, 0, Carrying: true),
            new LinkReport("Nord 2", Links.Bluetooth, 0, 0, Carrying: false),
        ]));

        Value(lines, "codec").ShouldBe("libopus");
        Value(lines, "bitrate").ShouldBe("160 kbps");
        Value(lines, "encode").ShouldBe("2.1 ms / 20 ms");
        Value(lines, "Nord 2 · Wi-Fi").ShouldBe("50 fps, queue 0");
        Value(lines, "Nord 2 · Bluetooth").ShouldBe("0 fps, queue 0 (idle)");
    }

    [Fact]
    public void Listener_ShowsBufferDriftAndCounters()
    {
        var lines = DiagnosticsText.Lines(new ListenerReport(false, Links.Wifi | Links.Bluetooth, 0, 49.6, 162, 204, 200, -35.4, 3, 1, 0, 0, 0));

        Value(lines, "codec").ShouldBe("managed (fallback)");
        Value(lines, "links").ShouldBe("Wi-Fi + Bluetooth");
        Value(lines, "wifi").ShouldBe("50 fps");
        Value(lines, "buffer").ShouldBe("204 / 200 ms");
        Value(lines, "drift").ShouldBe("-35 ppm");
        Value(lines, "caught up").ShouldBe("3");
    }

    [Theory]
    [InlineData(Links.None, "-")]
    [InlineData(Links.Bluetooth, "Bluetooth")]
    [InlineData(Links.Wifi, "Wi-Fi")]
    public void Describe_NamesTheLinks(Links links, string expected)
    {
        DiagnosticsText.Describe(links).ShouldBe(expected);
    }

    [Fact]
    public void Numbers_DoNotDependOnThePhonesLanguage()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var lines = DiagnosticsText.Lines(new MasterReport(true, 96, 1.5, 0, []));

            Value(lines, "encode").ShouldBe("1.5 ms / 20 ms");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
