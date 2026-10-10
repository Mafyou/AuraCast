namespace AuraMusic.Mobile.Casting;

/// <summary>
/// Listener side: finds the master among the paired phones, connects over Bluetooth Classic RFCOMM,
/// decodes the Opus frames and plays them. Reconnects automatically when the master goes away.
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

    // Spectrum levels waiting for their audio to be played; bounded in case the AudioTrack stalls.
    const int MaxPendingLevels = 50;

    CancellationTokenSource? cts;
    readonly OutputGain gain = new();
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

        var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
        if (adapter is not { IsEnabled: true })
        {
            AuraHub.Publish(new Failed(AppStrings.ErrorBluetoothOffListen));
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        cts = new CancellationTokenSource();
        var stoppingToken = cts.Token;
        _ = Task.Run(() => RunAsync(adapter, stoppingToken));
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

    async Task RunAsync(BluetoothAdapter adapter, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                AuraHub.Publish(new Searching());
                using var socket = ConnectToMaster(adapter, stoppingToken);
                if (socket is not null)
                    Listen(socket, gain, stoppingToken);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                return; // stopping closes the socket under the reader: whatever it throws then is expected
            }
            catch (InvalidDataException ex)
            {
                AuraHub.Publish(new Failed(ex.Message));
            }
            catch (Exception ex) when (ex is IOException or Java.IO.IOException)
            {
                // The master stopped or went out of range (streams wrap Java's IOException in System.IO's).
            }
            catch (Exception ex)
            {
                AuraHub.Publish(new Failed(ex.Message));
                StopSelf();
                return;
            }

            // Master not broadcasting yet, stopped, or out of range: look for it again.
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Tries each paired phone (last master first) until one accepts on the AuraMusic service.</summary>
    static BluetoothSocket? ConnectToMaster(BluetoothAdapter adapter, CancellationToken stoppingToken)
    {
        // Checked first: with Bluetooth off the paired list reads empty, which would wrongly ask to pair.
        if (!adapter.IsEnabled)
            throw new InvalidOperationException(AppStrings.ErrorBluetoothOffListen);

        var phones = adapter.BondedDevices?
            .Where(device => device.BluetoothClass is { } deviceClass && MasterDeviceClasses.Contains(deviceClass.MajorDeviceClass))
            .ToList() ?? [];
        if (phones.Count == 0)
            throw new InvalidOperationException(AppStrings.ErrorPairFirst);

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

    static void Listen(BluetoothSocket socket, OutputGain gain, CancellationToken stoppingToken)
    {
        using var closeOnStop = stoppingToken.Register(socket.Close);
        var stream = socket.InputStream!;
        AuraProtocol.ReadHeader(stream);
        AuraHub.Publish(new Listening(socket.RemoteDevice?.Name ?? "AuraMusic"));
        Receive(stream, gain, stoppingToken);
    }

    static void Receive(System.IO.Stream stream, OutputGain gain, CancellationToken stoppingToken)
    {
        var stats = new ReceiveStats();
        var packets = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(MaxBufferedFrames) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        var playback = new Thread(() => Play(packets.Reader, stats, gain, stoppingToken)) { IsBackground = true, Name = "AuraMusic playback" };
        playback.Start();

        try
        {
            var buffer = new byte[AuraProtocol.MaxPacketSize];
            while (!stoppingToken.IsCancellationRequested)
            {
                int length = AuraProtocol.ReadFrame(stream, buffer);
                if (packets.Reader.Count == MaxBufferedFrames)
                    stats.Dropped++;
                packets.Writer.TryWrite(buffer.AsSpan(0, length).ToArray());
                stats.Received(length);
            }
        }
        finally
        {
            packets.Writer.TryComplete();
            playback.Join(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Decodes on the playback clock, so a packet that is not there in time gets concealed by Opus
    /// instead of cutting the sound.
    /// </summary>
    static void Play(ChannelReader<byte[]> packets, ReceiveStats stats, OutputGain gain, CancellationToken stoppingToken)
    {
        // A late wake-up of this thread starves the AudioTrack, which is heard as crackling:
        // run it at Android's audio priority, like any music player (not urgent-audio, which is the mixer's).
        global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);

        // Float output: codec overshoots cannot clip, and it is what the Android mixer works in anyway.
        int minBuffer = AudioTrack.GetMinBufferSize(AuraProtocol.SampleRate, ChannelOut.Stereo, AudioEncoding.PcmFloat);
        int hundredMs = AuraProtocol.SampleRate / 10 * AuraProtocol.Channels * sizeof(float);
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

        var decoder = OpusCodecFactory.CreateDecoder(AuraProtocol.SampleRate, AuraProtocol.Channels);
        var pcm = new float[AuraProtocol.FrameSamples * AuraProtocol.Channels];

        var playout = new PlayoutController(stats);
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
                        break;
                    case Skip(var data):
                        decoder.Decode(data, pcm, AuraProtocol.FrameSamples);
                        continue;
                    case Rebuffer:
                        Prebuffer(packets, playout.PrebufferFrames, stoppingToken);
                        continue;
                }

                if (gain.Value != volume)
                    track.SetVolume(volume = gain.Value); // audio focus: ducked, silenced or back
                track.Write(pcm, 0, length, WriteMode.Blocking); // paces the loop to the playback clock
                written += (uint)(length / AuraProtocol.Channels);

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
                uint played = (uint)track.PlaybackHeadPosition;
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

    sealed class ReceiveStats : IPlayoutMetrics
    {
        long windowStart = Environment.TickCount64;
        int frames, bytes;
        int caughtUp, concealed, skipped, rebuffers;
        public int Dropped;

        public void CaughtUp() => caughtUp++;

        public void Concealed() => concealed++;

        public void Skipped() => skipped++;

        public void Rebuffered() => rebuffers++;

        public void Received(int packetBytes)
        {
            frames++;
            bytes += packetBytes + 2;
            long elapsed = Environment.TickCount64 - windowStart;
            if (elapsed < 5_000)
                return;

            // 50 frames/s means the link keeps up with real time.
            Log.Info(AuraLog.Tag, $"rx {frames * 1000.0 / elapsed:F1} frames/s, {bytes * 8.0 / elapsed:F0} kbps, "
                + $"caught up {caughtUp}, concealed {concealed}, skipped {skipped}, rebuffers {rebuffers}, dropped {Dropped}");
            windowStart = Environment.TickCount64;
            frames = bytes = caughtUp = concealed = skipped = rebuffers = Dropped = 0;
        }
    }

    public override void OnDestroy()
    {
        cts?.Cancel();
        if (focusRequest is not null)
            ((AudioManager)GetSystemService(AudioService)!).AbandonAudioFocusRequest(focusRequest);
        if (noisyReceiver is not null)
            UnregisterReceiver(noisyReceiver);
        if (AuraHub.Current is not Failed) // keep the error on screen
            AuraHub.Publish(new Idle());
        base.OnDestroy();
    }

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
