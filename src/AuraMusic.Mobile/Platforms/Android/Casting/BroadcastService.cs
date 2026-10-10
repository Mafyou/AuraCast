namespace AuraMusic.Mobile.Casting;

/// <summary>
/// Master side: captures what the phone plays (YouTube Music, Spotify…), encodes it to Opus
/// and pushes it to every listener, over Bluetooth Classic RFCOMM and, when on the same Wi-Fi, over TCP too:
/// listeners connected both ways keep the first copy of each numbered packet.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaProjection)]
public sealed class BroadcastService : Service
{
    public const string ExtraResultCode = "resultCode";
    public const string ExtraResultData = "resultData";

    readonly List<ListenerLink> listeners = [];
    CancellationTokenSource? cts;
    MediaProjection? projection;
    AudioRecord? recorder;
    Thread? captureThread;

    // About a third of the CPU of complexity 10, so no phone falls behind; the difference is not audible.
    const int EncoderComplexity = 5;

    const int FrameLength = AuraProtocol.FrameSamples * AuraProtocol.Channels;

    // Captured frames are rented, not allocated: 50 fresh arrays a second kept the GC busy, and on Android
    // every .NET collection also stops the Java side, audio threads included.
    static readonly ArrayPool<short> FramePool = ArrayPool<short>.Shared;

    // Up to 500 ms of captured audio waiting for the encoder; past that the oldest is dropped (and returned).
    readonly Channel<short[]> toEncode = Channel.CreateBounded<short[]>(
        new BoundedChannelOptions(25) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true },
        dropped => FramePool.Return(dropped));
    BluetoothServerSocket? server;
    LanServer? lan;

    // Identifies this broadcast: a listener following it over two links only merges packets of the same session.
    readonly uint session = (uint)Random.Shared.NextInt64(uint.MaxValue + 1L);

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent is null || intent.Action == AuraNotifications.ActionStop || cts is not null)
        {
            if (intent?.Action == AuraNotifications.ActionStop)
                StopSelf();
            return StartCommandResult.NotSticky;
        }

        // Android 14+: the foreground service must be running before the projection is created.
        AuraNotifications.StartForeground(this, AppStrings.NotificationBroadcasting, ForegroundService.TypeMediaProjection);
        try
        {
            Start(intent);
        }
        catch (Exception ex)
        {
            AuraHub.Publish(new Failed(ex.Message));
            StopSelf();
        }
        return StartCommandResult.NotSticky;
    }

    void Start(Intent intent)
    {
        var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
        if (adapter is not { IsEnabled: true })
            throw new InvalidOperationException(AppStrings.ErrorBluetoothOffBroadcast);

        var resultCode = intent.GetIntExtra(ExtraResultCode, 0);
#pragma warning disable CA1422 // the typed overload only exists on API 33+
        var resultData = (Intent)intent.GetParcelableExtra(ExtraResultData)!;
#pragma warning restore CA1422
        var projectionManager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
        projection = projectionManager.GetMediaProjection(resultCode, resultData)
            ?? throw new InvalidOperationException(AppStrings.ErrorCaptureDenied);
        projection.RegisterCallback(new ProjectionStoppedCallback(this), null);

        recorder = CreateRecorder(projection);
        // BLE L2CAP topped out at ~14 kbps on our phones; RFCOMM over BR/EDR easily carries the stream.
        // Listeners find us among their paired devices through this SDP record.
        server = adapter.ListenUsingInsecureRfcommWithServiceRecord("AuraMusic", Java.Util.UUID.FromString(AuraProtocol.ServiceUuid.ToString()))!;

        cts = new CancellationTokenSource();
        var stoppingToken = cts.Token;
        _ = Task.Run(() => AcceptLoop(stoppingToken));
        StartLan(stoppingToken);
        captureThread = new Thread(() => CaptureLoop(stoppingToken)) { IsBackground = true, Name = "AuraMusic capture" };
        captureThread.Start();
        _ = Task.Run(() => EncodeLoop(stoppingToken));

        AuraHub.Publish(new Advertising());
    }

    static AudioRecord CreateRecorder(MediaProjection projection)
    {
        var config = new AudioPlaybackCaptureConfiguration.Builder(projection)
            .AddMatchingUsage(AudioUsageKind.Media)!
            .AddMatchingUsage(AudioUsageKind.Game)!
            .AddMatchingUsage(AudioUsageKind.Unknown)!
            .Build();

        var format = new AudioFormat.Builder()
            .SetEncoding(AudioEncoding.Pcm16bit)!
            .SetSampleRate(AuraProtocol.SampleRate)!
            .SetChannelMask((ChannelOut)ChannelIn.Stereo)! // the binding types the mask as ChannelOut
            .Build()!;

        int minBuffer = AudioRecord.GetMinBufferSize(AuraProtocol.SampleRate, ChannelIn.Stereo, AudioEncoding.Pcm16bit);

        return new AudioRecord.Builder()
            .SetAudioFormat(format)!
            .SetBufferSizeInBytes(Math.Max(minBuffer, AuraProtocol.FrameSamples * AuraProtocol.Channels * sizeof(short)) * 4)!
            .SetAudioPlaybackCaptureConfig(config)!
            .Build()!;
    }

    /// <summary>Wi-Fi is a bonus: if the server cannot start, Bluetooth alone carries the stream as before.</summary>
    void StartLan(CancellationToken stoppingToken)
    {
        try
        {
            lan = new LanServer(session, DeviceName.Of(this));
            lan.Start((stream, name, close) => AddListener(new ListenerLink(stream, name, close), stoppingToken), stoppingToken);
        }
        catch (SocketException ex)
        {
            Log.Warn(AuraLog.Tag, $"Wi-Fi link unavailable: {ex.Message}");
        }
    }

    void AddListener(ListenerLink link, CancellationToken stoppingToken)
    {
        lock (listeners)
            listeners.Add(link);
        PublishListeners();

        _ = Task.Run(() => link.Run(session, stoppingToken)).ContinueWith(_ =>
        {
            lock (listeners)
                listeners.Remove(link);
            link.Dispose();
            if (!stoppingToken.IsCancellationRequested)
                PublishListeners();
        }, TaskScheduler.Default);
    }

    void AcceptLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BluetoothSocket socket;
            try
            {
                socket = server!.Accept()!;
            }
            catch (Java.IO.IOException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Java.IO.IOException ex)
            {
                // Usually Bluetooth being turned off. Without a server nobody can join any more: stop for real
                // instead of capturing and encoding in the background behind an "error" screen.
                var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
                AuraHub.Publish(new Failed(adapter is { IsEnabled: true }
                    ? string.Format(CultureInfo.CurrentCulture, AppStrings.ErrorConnectionLost, ex.Message)
                    : AppStrings.ErrorBluetoothOffBroadcast));
                StopSelf();
                return;
            }

            AddListener(new ListenerLink(socket.OutputStream!, socket.RemoteDevice?.Name ?? "?", socket.Close), stoppingToken);
        }
    }

    void PublishListeners()
    {
        ImmutableArray<string> names;
        lock (listeners)
            names = [.. listeners.Select(link => link.Name).Distinct()]; // one phone may be on both links
        AuraHub.Publish(names.IsEmpty ? new Advertising() : new Streaming(names));
    }

    void CaptureLoop(CancellationToken stoppingToken)
    {
        // Audio priority keeps the capture on time, but not urgent-audio: that one belongs to Android's own
        // mixer and to the music app, which must never be starved by us.
        global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);

        var analyzer = new SpectrumAnalyzer(SpectrumHub.Bands, AuraProtocol.SampleRate, AuraProtocol.Channels);
        int captured = 0;

        recorder!.StartRecording();
        while (!stoppingToken.IsCancellationRequested)
        {
            var pcm = FramePool.Rent(FrameLength); // may be longer than a frame: only FrameLength is used
            for (int read = 0; read < FrameLength;)
            {
                int count = recorder.Read(pcm, read, FrameLength - read);
                if (count <= 0)
                {
                    FramePool.Return(pcm);
                    toEncode.Writer.TryComplete();
                    return; // stopped, or the recorder died
                }
                read += count;
            }

            // The visualizer only needs 25 updates a second, and none while nothing shows it.
            if (++captured % 2 == 0 && SpectrumHub.IsObserved)
            {
                var levels = new float[SpectrumHub.Bands];
                analyzer.Analyze(pcm.AsSpan(0, FrameLength), levels);
                SpectrumHub.Publish(levels);
            }

            bool anyListener;
            lock (listeners)
                anyListener = listeners.Count > 0;
            if (!anyListener || !toEncode.Writer.TryWrite(pcm))
                FramePool.Return(pcm);
        }
        toEncode.Writer.TryComplete();
    }

    /// <summary>
    /// Encoding is the heavy part: it runs at normal priority, so if the phone is busy only the listeners
    /// may hiccup, never the sound of the phone itself.
    /// </summary>
    async Task EncodeLoop(CancellationToken stoppingToken)
    {
        var encoder = OpusCodecFactory.CreateEncoder(AuraProtocol.SampleRate, AuraProtocol.Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = AuraProtocol.Bitrate;
        encoder.Complexity = EncoderComplexity;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;
        encoder.MaxBandwidth = OpusBandwidth.OPUS_BANDWIDTH_FULLBAND; // never trade away the highs

        var packet = new byte[AuraProtocol.MaxPacketSize];
        uint sequence = 0;
        long windowStart = Environment.TickCount64;
        var encoding = TimeSpan.Zero;
        int frames = 0;
        await foreach (var pcm in toEncode.Reader.ReadAllAsync(stoppingToken))
        {
            long started = Stopwatch.GetTimestamp();
            int length = encoder.Encode(pcm.AsSpan(0, FrameLength), AuraProtocol.FrameSamples, packet, packet.Length);
            encoding += Stopwatch.GetElapsedTime(started);
            FramePool.Return(pcm);

            var frame = new EncodedFrame(sequence++, packet.AsSpan(0, length).ToArray());
            ListenerLink[] targets;
            lock (listeners)
                targets = [.. listeners];
            foreach (var target in targets)
                target.Enqueue(frame);

            if (++frames > 0 && Environment.TickCount64 - windowStart >= 5_000)
            {
                // Must stay well under 20 ms per frame, or the listeners fall behind.
                Log.Info(AuraLog.Tag, $"encode {encoding.TotalMilliseconds / frames:F1} ms/frame, backlog {toEncode.Reader.Count}");
                windowStart = Environment.TickCount64;
                encoding = TimeSpan.Zero;
                frames = 0;
            }
        }
    }

    public override void OnDestroy()
    {
        cts?.Cancel();

        server?.Close();
        lan?.Dispose();

        lock (listeners)
        {
            foreach (var link in listeners)
                link.Dispose();
            listeners.Clear();
        }

        try { recorder?.Stop(); } catch (Java.Lang.IllegalStateException) { }
        captureThread?.Join(TimeSpan.FromSeconds(1));
        recorder?.Release();
        projection?.Stop();

        if (AuraHub.Current is not Failed) // keep the error on screen
            AuraHub.Publish(new Idle());
        base.OnDestroy();
    }

    sealed class ProjectionStoppedCallback(Service service) : MediaProjection.Callback
    {
        public override void OnStop() => service.StopSelf();
    }
}
