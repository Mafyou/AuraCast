namespace AuraMusic.Mobile.Casting;

/// <summary>
/// Master side: captures what the phone plays (YouTube Music, Spotify…), encodes it to Opus
/// and pushes it to every listener connected over a Bluetooth Classic RFCOMM socket.
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
    BluetoothServerSocket? server;

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
        captureThread = new Thread(() => CaptureLoop(stoppingToken)) { IsBackground = true, Name = "AuraMusic capture", Priority = System.Threading.ThreadPriority.Highest };
        captureThread.Start();

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
                AuraHub.Publish(new Failed(string.Format(CultureInfo.CurrentCulture, AppStrings.ErrorConnectionLost, ex.Message)));
                return;
            }

            var link = new ListenerLink(socket);
            lock (listeners)
                listeners.Add(link);
            PublishListeners();

            _ = Task.Run(() => link.Run(stoppingToken)).ContinueWith(_ =>
            {
                lock (listeners)
                    listeners.Remove(link);
                link.Dispose();
                if (!stoppingToken.IsCancellationRequested)
                    PublishListeners();
            }, TaskScheduler.Default);
        }
    }

    void PublishListeners()
    {
        int count;
        lock (listeners)
            count = listeners.Count;
        AuraHub.Publish(count == 0 ? new Advertising() : new Streaming(count));
    }

    void CaptureLoop(CancellationToken stoppingToken)
    {
        // Same as playback: never let the capture fall behind the audio clock.
        global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.UrgentAudio);

        var encoder = OpusCodecFactory.CreateEncoder(AuraProtocol.SampleRate, AuraProtocol.Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = AuraProtocol.Bitrate;
        encoder.Complexity = 10;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;
        encoder.MaxBandwidth = OpusBandwidth.OPUS_BANDWIDTH_FULLBAND; // never trade away the highs

        var pcm = new short[AuraProtocol.FrameSamples * AuraProtocol.Channels];
        var packet = new byte[AuraProtocol.MaxPacketSize];
        var analyzer = new SpectrumAnalyzer(SpectrumHub.Bands, AuraProtocol.SampleRate, AuraProtocol.Channels);
        int captured = 0;

        recorder!.StartRecording();
        while (!stoppingToken.IsCancellationRequested)
        {
            for (int read = 0; read < pcm.Length;)
            {
                int count = recorder.Read(pcm, read, pcm.Length - read);
                if (count <= 0)
                    return; // stopped, or the recorder died
                read += count;
            }

            if (++captured % 2 == 0) // the visualizer only needs 25 updates a second
            {
                var levels = new float[SpectrumHub.Bands];
                analyzer.Analyze(pcm, levels);
                SpectrumHub.Publish(levels);
            }

            ListenerLink[] targets;
            lock (listeners)
                targets = [.. listeners];
            if (targets.Length == 0)
                continue;

            int length = encoder.Encode(pcm, AuraProtocol.FrameSamples, packet, packet.Length);
            var frame = packet.AsSpan(0, length).ToArray();
            foreach (var target in targets)
                target.Enqueue(frame);
        }
    }

    public override void OnDestroy()
    {
        cts?.Cancel();

        server?.Close();

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

    /// <summary>One connected listener, with its own small queue so a slow Bluetooth link never stalls the capture.</summary>
    sealed class ListenerLink(BluetoothSocket socket) : IDisposable
    {
        const int QueueCapacity = 25;
        const int MaxFramesPerWrite = 10;

        // ~500 ms of audio; beyond that we drop the oldest frames rather than drift behind.
        readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        int dropped;

        public void Enqueue(byte[] frame)
        {
            if (queue.Reader.Count == QueueCapacity)
                Interlocked.Increment(ref dropped);
            queue.Writer.TryWrite(frame);
        }

        public async Task Run(CancellationToken stoppingToken)
        {
            var stream = socket.OutputStream!;
            AuraProtocol.WriteHeader(stream);

            // Send whatever has queued up as a single write: batches grow by themselves
            // when the link falls behind, which costs far less than one write per frame.
            var batch = new MemoryStream();
            long windowStart = Environment.TickCount64;
            int sent = 0, writes = 0;
            var writing = TimeSpan.Zero;
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                batch.SetLength(0);
                for (int count = 0; count < MaxFramesPerWrite && queue.Reader.TryRead(out var frame); count++)
                {
                    AuraProtocol.WriteFrame(batch, frame);
                    sent++;
                }
                long writeStart = Stopwatch.GetTimestamp();
                stream.Write(batch.GetBuffer(), 0, (int)batch.Length);
                stream.Flush();
                writing += Stopwatch.GetElapsedTime(writeStart);
                writes++;

                long elapsed = Environment.TickCount64 - windowStart;
                if (elapsed >= 5_000)
                {
                    // Below 50 frames/s the Bluetooth link cannot keep up with real time.
                    Log.Info(AuraLog.Tag, $"tx {sent * 1000.0 / elapsed:F1} frames/s, {(double)sent / writes:F1} frames/write, {writing.TotalMilliseconds / writes:F1} ms/write, "
                        + $"queued {queue.Reader.Count}, dropped {Interlocked.Exchange(ref dropped, 0)}");
                    windowStart = Environment.TickCount64;
                    sent = writes = 0;
                    writing = TimeSpan.Zero;
                }
            }
        }

        public void Dispose()
        {
            queue.Writer.TryComplete();
            socket.Close();
        }
    }

    sealed class ProjectionStoppedCallback(Service service) : MediaProjection.Callback
    {
        public override void OnStop() => service.StopSelf();
    }
}
