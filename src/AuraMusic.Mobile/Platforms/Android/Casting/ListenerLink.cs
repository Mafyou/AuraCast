namespace AuraMusic.Mobile.Casting;

/// <summary>An encoded 20 ms packet and its place in the stream.</summary>
readonly record struct EncodedFrame(uint Sequence, byte[] Packet);

/// <summary>
/// One connected listener, over Bluetooth or Wi-Fi, with its own small queue so a slow link never stalls
/// the capture or the other listeners. A phone connected both ways has two of these, with the same <see cref="Id"/>.
/// </summary>
sealed class ListenerLink(Stream input, Stream output, Links kind, Action close) : IDisposable
{
    const int QueueCapacity = 50;
    const int MaxFramesPerWrite = 10;
    // A Wi-Fi link whose queue stays this short delivers in real time.
    const int HealthyQueueDepth = 2;
    static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(3);
    static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(1);

    // A second of audio, to lose nothing over a radio stall (the listener gets back in step by itself);
    // beyond that the oldest frames are dropped rather than drift behind.
    readonly Channel<EncodedFrame> queue = Channel.CreateBounded<EncodedFrame>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    Hello? hello;
    long readyAt;
    int dropped;
    int sentTotal, sentReported;

    public Links Kind => kind;

    /// <summary>The listener's phone: the same over Bluetooth and Wi-Fi.</summary>
    public Guid Id => hello?.Id ?? Guid.Empty;

    /// <summary>As shown on the master's screen.</summary>
    public string Name => hello?.Name ?? "?";

    /// <summary>
    /// A Wi-Fi link that has been keeping up for a few seconds: the same phone's Bluetooth link can then idle
    /// (it only gets keep-alives) and takes over again the moment this one falls behind.
    /// </summary>
    public bool IsHealthyWifi => kind == Links.Wifi
        && queue.Reader.Count <= HealthyQueueDepth
        && Environment.TickCount64 - readyAt >= HealthyAfter.TotalMilliseconds;

    /// <summary>Sends the header and waits for the listener to say who it is. Blocking.</summary>
    public Hello Handshake(uint session)
    {
        AuraProtocol.WriteHeader(output, session);
        hello = AuraProtocol.ReadHello(input);
        readyAt = Environment.TickCount64;
        return hello;
    }

    /// <summary>For the diagnostics screen: what this link sent since the previous call.</summary>
    public LinkReport Report(double seconds, bool carrying)
    {
        int total = Volatile.Read(ref sentTotal);
        int sent = total - Interlocked.Exchange(ref sentReported, total);
        return new LinkReport(Name, kind, sent / seconds, queue.Reader.Count, carrying);
    }

    public void Enqueue(EncodedFrame frame)
    {
        if (queue.Reader.Count == QueueCapacity)
            Interlocked.Increment(ref dropped);
        queue.Writer.TryWrite(frame);
    }

    public async Task Run(CancellationToken stoppingToken)
    {
        // Send whatever has queued up as a single write: batches grow by themselves
        // when the link falls behind, which costs far less than one write per frame.
        var batch = new MemoryStream();
        long windowStart = Environment.TickCount64;
        int sent = 0, writes = 0;
        var writing = TimeSpan.Zero;
        Task<bool>? waiting = null;
        while (true)
        {
            // No sound to send (music paused, or this link idles behind a healthy Wi-Fi one): a keep-alive every
            // second tells the listener the link is quiet, not dead, and lets a dead one fail on write.
            waiting ??= queue.Reader.WaitToReadAsync(stoppingToken).AsTask();
            try
            {
                if (!await waiting.WaitAsync(KeepAliveInterval, stoppingToken))
                    return;
                waiting = null;
            }
            catch (TimeoutException)
            {
                AuraProtocol.WriteKeepAlive(output);
                continue;
            }

            batch.SetLength(0);
            for (int count = 0; count < MaxFramesPerWrite && queue.Reader.TryRead(out var frame); count++)
            {
                AuraProtocol.WriteFrame(batch, frame.Sequence, frame.Packet);
                sent++;
                Interlocked.Increment(ref sentTotal);
            }
            long writeStart = Stopwatch.GetTimestamp();
            output.Write(batch.GetBuffer(), 0, (int)batch.Length);
            output.Flush();
            writing += Stopwatch.GetElapsedTime(writeStart);
            writes++;

            long elapsed = Environment.TickCount64 - windowStart;
            if (elapsed >= 5_000)
            {
                // Below 50 frames/s the link cannot keep up with real time (or it idles behind Wi-Fi).
                Log.Info(AuraLog.Tag, $"tx {Name} {kind}: {sent * 1000.0 / elapsed:F1} frames/s, {(double)sent / writes:F1} frames/write, "
                    + $"{writing.TotalMilliseconds / writes:F1} ms/write, queued {queue.Reader.Count}, dropped {Interlocked.Exchange(ref dropped, 0)}");
                windowStart = Environment.TickCount64;
                sent = writes = 0;
                writing = TimeSpan.Zero;
            }
        }
    }

    public void Dispose()
    {
        queue.Writer.TryComplete();
        close();
    }
}
