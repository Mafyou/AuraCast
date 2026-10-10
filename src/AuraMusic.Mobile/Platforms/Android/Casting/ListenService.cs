namespace AuraMusic.Mobile.Casting;

/// <summary>
/// Listener side: follows the master over Bluetooth Classic RFCOMM and, when both phones are on the same
/// Wi-Fi, over TCP at the same time. Each link feeds the same jitter buffer through a <see cref="SequenceGate"/>,
/// so the first copy of every packet is played and a stalling link costs nothing. Reconnects on its own.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class ListenService : Service
{
    // Jitter buffer capacity, in 20 ms Opus packets; the playout policy lives in PlayoutController.
    // Room for the burst that follows a Bluetooth stall (800 ms): a smaller channel drops those packets.
    const int MaxBufferedFrames = 40;
    // Most of the cushion sits in the AudioTrack, not in the channel: a late packet is waited for until the
    // AudioTrack is about to run dry, and only then concealed.
    const int LowWaterFrames = AuraProtocol.SampleRate / 50; // 20 ms

    // Catching up on latency: 1 ms shaved off a 20 ms packet behind a 2 ms crossfade, inaudible on music.
    const int CatchUpFrames = AuraProtocol.SampleRate / 1000;
    const int CatchUpFadeFrames = 2 * CatchUpFrames;

    /// <summary>Paired devices that can be a master: phones, and tablets (which report themselves as computers).</summary>
    static readonly FrozenSet<MajorDeviceClass> MasterDeviceClasses = [MajorDeviceClass.Phone, MajorDeviceClass.Computer];
    const string LastMasterKey = "lastMasterAddress";

    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    // After a link found another broadcast than the one being followed, or an incompatible master.
    static readonly TimeSpan LongRetryDelay = TimeSpan.FromSeconds(15);
    static readonly TimeSpan WifiConnectTimeout = TimeSpan.FromSeconds(3);
    // The master sends audio or a keep-alive at least every second: a link silent for this long is dead,
    // even if the socket has not noticed (Wi-Fi lost, phone out of range).
    static readonly TimeSpan LinkTimeout = TimeSpan.FromSeconds(5);

    // The playback rate is only touched for a real change: each call crosses into the audio system.
    const int MinRateStepHz = 2;

    // Spectrum levels waiting for their audio to be played; bounded in case the AudioTrack stalls.
    const int MaxPendingLevels = 50;

    CancellationTokenSource? cts;
    readonly OutputGain gain = new();
    readonly SequenceGate gate = new();
    readonly ReceiveStats stats = new();
    static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(1);
    Timer? reportTimer;
    readonly Channel<byte[]> packets = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(MaxBufferedFrames) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    readonly Lock linksLock = new();
    int bluetoothLinks, wifiLinks;
    string master = "AuraMusic";
    AudioFocusRequestClass? focusRequest;
    NoisyReceiver? noisyReceiver;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == AuraNotifications.ActionStop)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        if (cts is not null)
            return StartCommandResult.NotSticky;

        AuraNotifications.StartForeground(this, AppStrings.NotificationListening, ForegroundService.TypeMediaPlayback);
        RequestAudioFocus();
        WatchHeadphones();
        AuraHub.Publish(new Searching());

        cts = new CancellationTokenSource();
        var stoppingToken = cts.Token;
        new Thread(() => Play(packets.Reader, stats, gain, stoppingToken)) { IsBackground = true, Name = "AuraMusic playback" }.Start();
        reportTimer = new Timer(_ => stats.Publish(), null, ReportInterval, ReportInterval);
        _ = Task.Run(() => BluetoothLoopAsync(stoppingToken));
        _ = Task.Run(() => WifiLoopAsync(stoppingToken));
        return StartCommandResult.NotSticky;
    }

    /// <summary>Calls, navigation prompts or another music app lower or silence us instead of playing on top.</summary>
    void RequestAudioFocus()
    {
        var audio = (AudioManager)GetSystemService(AudioService)!;
        focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
            .SetAudioAttributes(new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)!
            .SetOnAudioFocusChangeListener(new FocusListener(gain))!
            .Build()!;
        audio.RequestAudioFocus(focusRequest);
    }

    /// <summary>Headphones unplugged or disconnected: stop rather than suddenly play out loud.</summary>
    void WatchHeadphones()
    {
        noisyReceiver = new NoisyReceiver(StopSelf);
        var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            RegisterReceiver(noisyReceiver, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(noisyReceiver, filter);
    }

    async Task BluetoothLoopAsync(CancellationToken stoppingToken)
    {
        var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
        while (!stoppingToken.IsCancellationRequested)
        {
            var retry = RetryDelay;
            try
            {
                using var socket = ConnectToMaster(adapter, stoppingToken);
                if (socket is not null)
                {
                    using var closeOnStop = stoppingToken.Register(socket.Close);
                    var name = socket.RemoteDevice?.Name ?? "AuraMusic";
                    if (!ReadLink(socket.InputStream!, socket.OutputStream!, Links.Bluetooth, name, socket.Close, stoppingToken))
                        retry = LongRetryDelay; // Wi-Fi follows another master: do not hammer this one
                }
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                return; // stopping closes the socket under the reader: whatever it throws then is expected
            }
            catch (InvalidDataException ex)
            {
                AuraHub.Publish(new Failed(ex.Message));
                StopSelf();
                return;
            }
            catch (Exception ex) when (ex is IOException or Java.IO.IOException)
            {
                // The master stopped or went out of range (streams wrap Java's IOException in System.IO's).
            }
            catch (UnavailableException ex)
            {
                // Bluetooth off or nothing paired: not fatal, the Wi-Fi link may still find the master.
                ReportProblem(ex.Message);
            }

            // Master not broadcasting yet, stopped, or out of range: look for it again.
            await Task.Delay(retry, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Tries each paired phone (last master first) until one accepts on the AuraMusic service.</summary>
    static BluetoothSocket? ConnectToMaster(BluetoothAdapter? adapter, CancellationToken stoppingToken)
    {
        // Checked first: with Bluetooth off the paired list reads empty, which would wrongly ask to pair.
        if (adapter is not { IsEnabled: true })
            throw new UnavailableException(AppStrings.ErrorBluetoothOffListen);
        var phones = adapter.BondedDevices?
            .Where(device => device.BluetoothClass is { } deviceClass && MasterDeviceClasses.Contains(deviceClass.MajorDeviceClass))
            .ToList() ?? [];
        if (phones.Count == 0)
            throw new UnavailableException(AppStrings.ErrorPairFirst);

        var lastMaster = Preferences.Get(LastMasterKey, null);
        var uuid = Java.Util.UUID.FromString(AuraProtocol.ServiceUuid.ToString());

        foreach (var phone in phones.OrderByDescending(phone => phone.Address == lastMaster))
        {
            stoppingToken.ThrowIfCancellationRequested();
            var socket = phone.CreateInsecureRfcommSocketToServiceRecord(uuid)!;
            using var closeOnStop = stoppingToken.Register(socket.Close);
            try
            {
                socket.Connect();
                Preferences.Set(LastMasterKey, phone.Address);
                return socket;
            }
            catch (Java.IO.IOException)
            {
                socket.Close(); // not broadcasting, or not in range
            }
        }
        return null;
    }

    /// <summary>Listens for masters' beacons on the local network and follows one over TCP.</summary>
    async Task WifiLoopAsync(CancellationToken stoppingToken)
    {
        // Some phones drop broadcast datagrams while the screen is off unless a multicast lock is held.
        var wifi = (global::Android.Net.Wifi.WifiManager?)GetSystemService(WifiService);
        // Held only while searching: it keeps the Wi-Fi radio busier, so it is released once connected.
        var multicast = wifi?.CreateMulticastLock("AuraMusic");
        multicast?.SetReferenceCounted(false);
        try
        {
            using var beacons = new UdpClient(AddressFamily.InterNetwork);
            beacons.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            beacons.Client.Bind(new IPEndPoint(IPAddress.Any, LanBeacon.UdpPort));

            while (!stoppingToken.IsCancellationRequested)
            {
                multicast?.Acquire();
                var received = await beacons.ReceiveAsync(stoppingToken);
                if (!LanBeacon.TryDecode(received.Buffer, out var beacon) || !IsOurMaster(beacon))
                    continue;
                multicast?.Release();
                try
                {
                    await FollowOverWifiAsync(received.RemoteEndPoint.Address, beacon, stoppingToken);
                }
                catch (InvalidDataException ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // An incompatible master: say so, and do not reconnect at every beacon.
                    ReportProblem(ex.Message);
                    await Task.Delay(LongRetryDelay, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested
                    && ex is IOException or SocketException or System.OperationCanceledException)
                {
                    // Lost the Wi-Fi link: Bluetooth (if any) carries on, and the next beacon reconnects.
                }
            }
        }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested || ex is SocketException)
        {
            // Stopping, or the beacon port is unavailable: Bluetooth alone carries the stream.
        }
        finally
        {
            multicast?.Release();
        }
    }

    /// <summary>The broadcast already followed (over Bluetooth or Wi-Fi), else the first master heard.</summary>
    bool IsOurMaster(LanBeacon beacon) => gate.Session is not { } following || beacon.Session == following;

    async Task FollowOverWifiAsync(IPAddress address, LanBeacon beacon, CancellationToken stoppingToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            connecting.CancelAfter(WifiConnectTimeout);
            await client.ConnectAsync(address, beacon.Port, connecting.Token);
        }
        using var closeOnStop = stoppingToken.Register(client.Dispose);
        var stream = client.GetStream();
        bool joined = await Task.Run(() => ReadLink(stream, stream, Links.Wifi, beacon.Name, client.Dispose, stoppingToken), stoppingToken);
        if (!joined)
            await Task.Delay(LongRetryDelay, stoppingToken); // refused (not paired yet) or another broadcast
    }

    /// <summary>Reads one link until it drops, letting through only the packets the other link has not delivered yet.</summary>
    /// <param name="close">Closes the link; also used when the master has been silent for too long.</param>
    /// <returns><see langword="false"/> when the link was not used: another broadcast is already being followed.</returns>
    bool ReadLink(Stream input, Stream output, Links link, string name, Action close, CancellationToken stoppingToken)
    {
        uint session = AuraProtocol.ReadHeader(input);
        if (!gate.Join(session))
            return false; // the other link follows another master: do not mix two broadcasts
        AuraProtocol.WriteHello(output, new Hello(DeviceName.Id, DeviceName.Of(this)));

        // A dead link does not always fail the read: close it ourselves when even keep-alives stop coming.
        long lastHeard = Environment.TickCount64;
        using var watchdog = new Timer(_ =>
        {
            if (Environment.TickCount64 - Interlocked.Read(ref lastHeard) > LinkTimeout.TotalMilliseconds)
                close();
        }, null, LinkTimeout, TimeSpan.FromSeconds(1));

        // Up only once the master has sent something: it closes the link right after the hello when it does
        // not admit this phone (Wi-Fi before any Bluetooth connection), and that must not flash "connected".
        bool up = false;
        try
        {
            var buffer = new byte[AuraProtocol.MaxPacketSize];
            while (!stoppingToken.IsCancellationRequested)
            {
                int length = AuraProtocol.ReadFrame(input, buffer, out uint sequence);
                Interlocked.Exchange(ref lastHeard, Environment.TickCount64);
                if (!up)
                {
                    up = true;
                    LinkUp(link, name);
                }
                if (length == 0)
                    continue; // keep-alive: the music is paused, or this link idles behind the other one
                stats.Received(link, length);
                if (!gate.TryAccept(session, sequence))
                    continue; // the other link was faster
                if (packets.Reader.Count == MaxBufferedFrames)
                    stats.Dropped();
                packets.Writer.TryWrite(buffer.AsSpan(0, length).ToArray());
            }
        }
        catch (IOException) when (!up && !stoppingToken.IsCancellationRequested)
        {
            return false; // refused, or gone before sending anything
        }
        finally
        {
            if (up)
                LinkDown(link);
            else
                ForgetBroadcastIfAlone();
        }
        return true;
    }

    /// <summary>A link joined a broadcast but never came up: free the gate unless the other link is using it.</summary>
    void ForgetBroadcastIfAlone()
    {
        lock (linksLock)
        {
            if (bluetoothLinks + wifiLinks == 0)
                gate.Reset();
        }
    }

    void LinkUp(Links link, string name)
    {
        lock (linksLock)
        {
            master = name;
            if (link == Links.Bluetooth)
                bluetoothLinks++;
            else
                wifiLinks++;
            PublishLinks();
        }
    }

    void LinkDown(Links link)
    {
        lock (linksLock)
        {
            if (link == Links.Bluetooth)
                bluetoothLinks--;
            else
                wifiLinks--;
            if (bluetoothLinks + wifiLinks == 0)
                gate.Reset(); // the next master found may be another broadcast
            PublishLinks();
        }
    }

    void PublishLinks()
    {
        if (cts?.IsCancellationRequested ?? true)
            return;
        var links = (bluetoothLinks > 0 ? Links.Bluetooth : Links.None) | (wifiLinks > 0 ? Links.Wifi : Links.None);
        stats.Links = links;
        AuraHub.Publish(links == Links.None ? new Searching() : new Listening(master, links));
    }

    /// <summary>Shown while searching, unless the other link is already playing.</summary>
    void ReportProblem(string problem)
    {
        lock (linksLock)
        {
            if (bluetoothLinks + wifiLinks == 0 && !(cts?.IsCancellationRequested ?? true))
                AuraHub.Publish(new Searching(problem));
        }
    }

    /// <summary>
    /// Decodes on the playback clock, so a packet that is not there in time gets concealed by Opus
    /// instead of cutting the sound. Runs for the whole service: while no link is up it just waits.
    /// </summary>
    static void Play(ChannelReader<byte[]> packets, ReceiveStats stats, OutputGain gain, CancellationToken stoppingToken)
    {
        // A late wake-up of this thread starves the AudioTrack, which is heard as crackling:
        // run it at Android's audio priority, like any music player (not urgent-audio, which is the mixer's).
        global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);

        // Float output: codec overshoots cannot clip, and it is what the Android mixer works in anyway.
        int minBuffer = AudioTrack.GetMinBufferSize(AuraProtocol.SampleRate, ChannelOut.Stereo, AudioEncoding.PcmFloat);
        int hundredMs = AuraProtocol.SampleRate / 10 * AuraProtocol.Channels * sizeof(float);
        int outputShareFrames = AuraProtocol.SampleRate * PlayoutTuning.OutputShareMs / 1000;
        using var track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)!
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(AudioEncoding.PcmFloat)!
                .SetSampleRate(AuraProtocol.SampleRate)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!)!
            .SetTransferMode(AudioTrackMode.Stream)!
            .SetBufferSizeInBytes(Math.Max(minBuffer * 4, hundredMs))!
            .Build();
        // Only a small, known share of the latency lives in the output: the rest is the jitter buffer, which
        // the sync setting and the drift controller can then actually steer.
        track.SetBufferSizeInFrames(outputShareFrames);
        // What the phone granted, which may be more than asked: everything below is planned around it.
        int outputMs = track.BufferSizeInFrames * 1000 / AuraProtocol.SampleRate;
        Log.Info(AuraLog.Tag, $"output buffer: {outputMs} ms (asked {PlayoutTuning.OutputShareMs})");

        using var decoder = OpusCodec.CreateDecoder(AuraProtocol.SampleRate, AuraProtocol.Channels);
        Log.Info(AuraLog.Tag, decoder.IsNative ? "decoder: libopus" : "decoder: managed fallback (libopus did not load)");
        stats.NativeCodec = decoder.IsNative;
        var pcm = new float[AuraProtocol.FrameSamples * AuraProtocol.Channels];

        int latency = PlayoutTuning.LatencyFor(stats.Links);
        int reachable = PlayoutTuning.ReachableLatencyMs(latency, outputMs);
        var playout = new PlayoutController(stats);
        playout.SetCushion(PlayoutTuning.CushionFrames(latency, outputMs));
        // Plays a hair faster or slower so the buffer stays on target despite the two phones' clocks differing.
        var drift = new DriftController(AuraProtocol.SampleRate) { TargetMs = reachable };
        int playbackRate = AuraProtocol.SampleRate;
        var analyzer = new SpectrumAnalyzer(SpectrumHub.Bands, AuraProtocol.SampleRate, AuraProtocol.Channels);
        // Levels wait here until their audio actually comes out of the speaker, so the visualizer is in sync.
        var pendingLevels = new Queue<(uint PlayedAt, float[] Levels)>();
        uint written = 0; // sample frames handed to the AudioTrack; wraps like its playback head
        float volume = 1;
        track.Play();
        try
        {
            Prebuffer(packets, playout.PrebufferFrames, stoppingToken);
            while (!packets.Completion.IsCompleted && !stoppingToken.IsCancellationRequested)
            {
                if (PlayoutTuning.LatencyFor(stats.Links) is var wanted && wanted != latency)
                {
                    latency = wanted; // the sync slider moved, or Wi-Fi came or went
                    reachable = PlayoutTuning.ReachableLatencyMs(latency, outputMs);
                    playout.SetCushion(PlayoutTuning.CushionFrames(latency, outputMs));
                    drift.TargetMs = reachable;
                }

                if (!packets.TryRead(out var packet))
                    packet = WaitForLatePacket(packets, track, written, stoppingToken);

                int length = pcm.Length;
                switch (playout.Next(packet, packets.Count))
                {
                    case Play(var data):
                        decoder.Decode(data, pcm, AuraProtocol.FrameSamples);
                        break;
                    case CatchUp(var data):
                        decoder.Decode(data, pcm, AuraProtocol.FrameSamples);
                        length = PcmCrossfade.Shorten(pcm, AuraProtocol.Channels, CatchUpFrames, CatchUpFadeFrames);
                        break;
                    case Conceal:
                        decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, AuraProtocol.FrameSamples);
                        drift.RanDry(); // nothing left to play: certainly not the moment to play faster
                        break;
                    case Skip(var data):
                        decoder.Decode(data, pcm, AuraProtocol.FrameSamples);
                        continue;
                    case Rebuffer:
                        stats.Playing(0, reachable, 0); // ran dry: say so rather than leave the last good figures on screen
                        Prebuffer(packets, playout.PrebufferFrames, stoppingToken);
                        drift.Restart();
                        continue;
                }

                if (gain.Value != volume)
                    track.SetVolume(volume = gain.Value); // audio focus: ducked, silenced or back
                track.Write(pcm, 0, length, WriteMode.Blocking); // paces the loop to the playback clock
                written += (uint)(length / AuraProtocol.Channels);

                uint played = (uint)track.PlaybackHeadPosition;
                double bufferedMs = (packets.Count * AuraProtocol.FrameSamples + (double)(written - played)) * 1000 / AuraProtocol.SampleRate;
                int rate = drift.Update(bufferedMs);
                if (Math.Abs(rate - playbackRate) >= MinRateStepHz)
                    track.SetPlaybackRate(playbackRate = rate);
                stats.Playing((int)bufferedMs, reachable, drift.Correction * 1e6);

                // The visualizer only needs 25 updates a second, and none while nothing shows it.
                if (!SpectrumHub.IsObserved)
                    pendingLevels.Clear();
                else if (written / AuraProtocol.FrameSamples % 2 == 0)
                {
                    var levels = new float[SpectrumHub.Bands];
                    analyzer.Analyze(pcm.AsSpan(0, length), levels);
                    if (pendingLevels.Count == MaxPendingLevels)
                        pendingLevels.Dequeue();
                    pendingLevels.Enqueue((written, levels));
                }
                while (pendingLevels.TryPeek(out var next) && (int)(played - next.PlayedAt) >= 0)
                    SpectrumHub.Publish(pendingLevels.Dequeue().Levels);
            }
        }
        finally
        {
            track.Stop();
            track.Release();
        }
    }

    /// <summary>
    /// Waits for a late packet as long as the AudioTrack still has audio to play; returns <see langword="null"/>
    /// (conceal) only when it is about to run dry.
    /// </summary>
    static byte[]? WaitForLatePacket(ChannelReader<byte[]> packets, AudioTrack track, uint written, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !packets.Completion.IsCompleted)
        {
            uint buffered = written - (uint)track.PlaybackHeadPosition;
            if (buffered < LowWaterFrames)
                return null;
            Thread.Sleep(1);
            if (packets.TryRead(out var packet))
                return packet;
        }
        return null;
    }

    static void Prebuffer(ChannelReader<byte[]> packets, int frames, CancellationToken stoppingToken)
    {
        while (packets.Count < frames && !packets.Completion.IsCompleted && !stoppingToken.IsCancellationRequested)
            Thread.Sleep(5);
    }

    /// <summary>
    /// Per-link reception and playout events: published every second for the diagnostics screen, logged every
    /// five (<c>adb logcat -s AuraMusic</c>).
    /// </summary>
    sealed class ReceiveStats : IPlayoutMetrics
    {
        const int LogEveryReports = 5;

        readonly Lock counters = new();
        long windowStart = Environment.TickCount64;
        int bluetooth, wifi, bytes, reports;
        int caughtUp, concealed, skipped, rebuffers, dropped; // since the service started
        int bufferedMs, targetMs;
        double driftPpm;

        public bool NativeCodec { get; set; }

        public Links Links { get; set; }

        public void CaughtUp() => Count(ref caughtUp);

        public void Concealed() => Count(ref concealed);

        public void Skipped() => Count(ref skipped);

        public void Rebuffered() => Count(ref rebuffers);

        public void Dropped() => Count(ref dropped);

        void Count(ref int counter)
        {
            lock (counters)
                counter++;
        }

        /// <summary>From the playback thread, once per packet played.</summary>
        public void Playing(int buffered, int target, double drift)
        {
            lock (counters)
                (bufferedMs, targetMs, driftPpm) = (buffered, target, drift);
        }

        public void Received(Links link, int packetBytes)
        {
            lock (counters)
            {
                if (link == Links.Bluetooth)
                    bluetooth++;
                else
                    wifi++;
                bytes += packetBytes + 6;
            }
        }

        /// <summary>
        /// Once a second, on a timer rather than on reception: when nothing arrives (music paused, link lost)
        /// the screen must show 0 frames/s, not the last good figures.
        /// </summary>
        public void Publish()
        {
            lock (counters)
            {
                long elapsed = Math.Max(1, Environment.TickCount64 - windowStart);
                // 50 frames/s on a link means it keeps up with real time on its own.
                var report = new ListenerReport(NativeCodec, Links, bluetooth * 1000.0 / elapsed, wifi * 1000.0 / elapsed,
                    (int)(bytes * 8.0 / elapsed), bufferedMs, targetMs, driftPpm, caughtUp, concealed, skipped, rebuffers, dropped);
                DiagnosticsHub.Publish(report);
                if (++reports % LogEveryReports == 0)
                    Log.Info(AuraLog.Tag, $"rx bluetooth {report.BluetoothFramesPerSecond:F0} + wifi {report.WifiFramesPerSecond:F0} frames/s, "
                        + $"{report.Kbps} kbps, buffer {bufferedMs}/{targetMs} ms, drift {driftPpm:+0;-0} ppm, caught up {caughtUp}, "
                        + $"concealed {concealed}, skipped {skipped}, rebuffers {rebuffers}, dropped {dropped}");
                windowStart = Environment.TickCount64;
                bluetooth = wifi = bytes = 0;
            }
        }
    }

    public override void OnDestroy()
    {
        cts?.Cancel();
        reportTimer?.Dispose();
        packets.Writer.TryComplete();
        if (focusRequest is not null)
            ((AudioManager)GetSystemService(AudioService)!).AbandonAudioFocusRequest(focusRequest);
        if (noisyReceiver is not null)
            UnregisterReceiver(noisyReceiver);
        if (AuraHub.Current is not Failed) // keep the error on screen
            AuraHub.Publish(new Idle());
        DiagnosticsHub.Publish(new NoReport());
        base.OnDestroy();
    }

    /// <summary>A link cannot be used right now (Bluetooth off, nothing paired); the other one may.</summary>
    sealed class UnavailableException(string message) : Exception(message);

    /// <summary>Output volume the audio focus asks for, read by the playback thread.</summary>
    sealed class OutputGain
    {
        public volatile float Value = 1;
    }

    sealed class FocusListener(OutputGain gain) : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange) => gain.Value = focusChange switch
        {
            AudioFocus.Gain => 1f,
            AudioFocus.LossTransientCanDuck => 0.2f, // a navigation prompt or a notification: duck
            _ => 0f,                                  // a call or another player: stay silent until it ends
        };
    }

    sealed class NoisyReceiver(Action onNoisy) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => onNoisy();
    }
}
