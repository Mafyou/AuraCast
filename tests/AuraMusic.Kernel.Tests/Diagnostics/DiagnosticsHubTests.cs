namespace AuraMusic.Kernel.Tests.Diagnostics;

public sealed class DiagnosticsHubTests
{
    [Fact]
    public void Publish_UpdatesCurrentAndNotifies()
    {
        DiagnosticsReport? received = null;
        void OnUpdated(DiagnosticsReport report) => received = report;
        var report = new MasterReport(NativeCodec: true, BitrateKbps: 160, EncodeMs: 2.1, EncodeBacklog: 0,
            Links: [new LinkReport("Nord 2", Links.Wifi, 50, 0, Carrying: true)]);

        DiagnosticsHub.Updated += OnUpdated;
        try
        {
            DiagnosticsHub.Publish(report);

            (DiagnosticsHub.Current is MasterReport { BitrateKbps: 160 }).ShouldBeTrue();
            (received is MasterReport { Links.Length: 1 }).ShouldBeTrue();
        }
        finally
        {
            DiagnosticsHub.Updated -= OnUpdated;
            DiagnosticsHub.Publish(new NoReport());
        }
    }

    [Fact]
    public void Reports_PatternMatchExhaustively()
    {
        DiagnosticsReport report = new ListenerReport(true, Links.Wifi | Links.Bluetooth, 0, 50, 162, 204, 200, 35, 0, 0, 0, 0, 0);

        var role = report switch
        {
            NoReport => "idle",
            MasterReport => "master",
            ListenerReport { Links: var links } => $"listener via {links}",
        };

        role.ShouldBe("listener via Bluetooth, Wifi");
    }
}
