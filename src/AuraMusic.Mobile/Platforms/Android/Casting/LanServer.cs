namespace AuraMusic.Mobile.Casting;

/// <summary>
/// Master side of the Wi-Fi link: a TCP server for listeners on the same network, announced every second
/// by a UDP beacon. Without Wi-Fi it simply finds nobody; Bluetooth keeps working on its own.
/// </summary>
sealed class LanServer(uint session, string name) : IDisposable
{
    static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(1);
    const int HelloTimeoutMs = 5_000;

    readonly TcpListener tcp = new(IPAddress.Any, 0);
    readonly UdpClient udp = new() { EnableBroadcast = true };

    /// <param name="onListener">A listener connected and said who it is: its stream, its name, how to close it.</param>
    public void Start(Action<Stream, string, Action> onListener, CancellationToken stoppingToken)
    {
        tcp.Start();
        var beacon = new LanBeacon(session, ((IPEndPoint)tcp.LocalEndpoint).Port, name).Encode();
        _ = Task.Run(() => AnnounceAsync(beacon, stoppingToken));
        _ = Task.Run(() => AcceptAsync(onListener, stoppingToken));
    }

    async Task AnnounceAsync(byte[] beacon, CancellationToken stoppingToken)
    {
        var everyone = new IPEndPoint(IPAddress.Broadcast, LanBeacon.UdpPort);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await udp.SendAsync(beacon, everyone, stoppingToken);
            }
            catch (SocketException)
            {
                // Not on Wi-Fi right now: keep announcing, it may come back.
            }
            await Task.Delay(BeaconInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    async Task AcceptAsync(Action<Stream, string, Action> onListener, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await tcp.AcceptTcpClientAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is System.OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            client.NoDelay = true; // 20 ms packets must not wait for Nagle
            _ = Task.Run(() =>
            {
                try
                {
                    var stream = client.GetStream();
                    stream.ReadTimeout = HelloTimeoutMs; // a silent connection is not one of ours
                    var listener = AuraProtocol.ReadHello(stream);
                    stream.ReadTimeout = Timeout.Infinite;
                    onListener(stream, listener, client.Dispose);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException)
                {
                    client.Dispose();
                }
            }, stoppingToken);
        }
    }

    public void Dispose()
    {
        tcp.Stop();
        udp.Dispose();
    }
}
