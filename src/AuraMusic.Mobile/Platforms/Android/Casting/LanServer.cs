namespace AuraMusic.Mobile.Casting;

/// <summary>
/// Master side of the Wi-Fi link: a TCP server for listeners on the same network, announced every second
/// by a UDP beacon. Without Wi-Fi it simply finds nobody; Bluetooth keeps working on its own.
/// </summary>
sealed class LanServer(uint session, string name) : IDisposable
{
    static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(1);
    const int HelloTimeoutMs = 5_000;
    // A listener that left without saying so (Wi-Fi lost) must not look connected for minutes.
    const int WriteTimeoutMs = 3_000;

    /// <summary>Interfaces of the mobile network: a beacon sent there reaches nobody and costs data.</summary>
    static readonly FrozenSet<string> MobileDataPrefixes = ["rmnet", "ccmni", "pdp", "v4-", "clat", "dummy", "tun"];

    readonly TcpListener tcp = new(IPAddress.Any, 0);
    readonly UdpClient anyInterface = new() { EnableBroadcast = true };

    /// <param name="onListener">A listener connected: its stream and how to close it.</param>
    public void Start(Action<Stream, Action> onListener, CancellationToken stoppingToken)
    {
        tcp.Start();
        var beacon = new LanBeacon(session, ((IPEndPoint)tcp.LocalEndpoint).Port, name).Encode();
        _ = Task.Run(() => AnnounceAsync(beacon, stoppingToken));
        _ = Task.Run(() => AcceptAsync(onListener, stoppingToken));
    }

    async Task AnnounceAsync(byte[] beacon, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool sent = false;
                foreach (var (local, broadcast) in LocalBroadcasts())
                {
                    // Bound to the interface's own address, so the beacon leaves through it and not through
                    // whichever network Android routes 255.255.255.255 to (often mobile data).
                    using var socket = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
                    await socket.SendAsync(beacon, new IPEndPoint(broadcast, LanBeacon.UdpPort), stoppingToken);
                    sent = true;
                }
                if (!sent)
                    await anyInterface.SendAsync(beacon, new IPEndPoint(IPAddress.Broadcast, LanBeacon.UdpPort), stoppingToken);
            }
            catch (Exception ex) when (ex is SocketException or NetworkInformationException)
            {
                // Not on Wi-Fi right now: keep announcing, it may come back.
            }
            await Task.Delay(BeaconInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Each local IPv4 address with the broadcast address of its subnet.</summary>
    static IEnumerable<(IPAddress Local, IPAddress Broadcast)> LocalBroadcasts()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || MobileDataPrefixes.Any(prefix => nic.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                    yield return (unicast.Address, LanBeacon.DirectedBroadcast(unicast.Address, Math.Clamp(unicast.PrefixLength, 0, 32)));
        }
    }

    async Task AcceptAsync(Action<Stream, Action> onListener, CancellationToken stoppingToken)
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
            var stream = client.GetStream();
            stream.ReadTimeout = HelloTimeoutMs; // a silent connection is not one of ours
            stream.WriteTimeout = WriteTimeoutMs;
            onListener(stream, client.Dispose);
        }
    }

    public void Dispose()
    {
        tcp.Stop();
        anyInterface.Dispose();
    }
}
