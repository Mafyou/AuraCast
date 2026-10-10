namespace AuraMusic.Mobile.Casting;

/// <summary>An encoded 20 ms packet and its place in the stream.</summary>
readonly record struct EncodedFrame(uint Sequence, byte[] Packet);

/// <summary>
/// One connected listener, over Bluetooth or Wi-Fi, with its own small queue so a slow link never stalls
/// the capture or the other listeners.
/// </summary>
sealed class ListenerLink(Stream output, string name, Action close) : IDisposable
{
    const int QueueCapacity = 25;
    const int MaxFramesPerWrite = 10;

    // ~500 ms of audio; beyond that we drop the oldest frames rather than drift behind.
    readonly Channel<EncodedFrame> queue = Channel.CreateBounded<EncodedFrame>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    int dropped;

    /// <summary>The listener's phone, as shown on the master's screen.</summary>
    public string Name => name;

    public void Enqueue(EncodedFrame frame)
    {
        if (queue.Reader.Count == QueueCapacity)
            Interlocked.Increment(ref dropped);
        queue.Writer.TryWrite(frame);
    }

    public async Task Run(uint session, CancellationToken stoppingToken)
    {
        AuraProtocol.WriteHeader(output, session);

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
                AuraProtocol.WriteFrame(batch, frame.Sequence, frame.Packet);
                sent++;
            }
            long writeStart = Stopwatch.GetTimestamp();
            output.Write(batch.GetBuffer(), 0, (int)batch.Length);
            output.Flush();
            writing += Stopwatch.GetElapsedTime(writeStart);
            writes++;

            long elapsed = Environment.TickCount64 - windowStart;
            if (elapsed >= 5_000)
            {
                // Below 50 frames/s the link cannot keep up with real time.
                Log.Info(AuraLog.Tag, $"tx {name}: {sent * 1000.0 / elapsed:F1} frames/s, {(double)sent / writes:F1} frames/write, "
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
