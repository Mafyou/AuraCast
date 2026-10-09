using System.Threading.Channels;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Util;
using AuraCast.Mobile.Core;
using Concentus;
using AudioEncoding = Android.Media.Encoding;
using Environment = System.Environment;

namespace AuraCast.Mobile.Casting;

/// <summary>
/// Listener side: finds the master among the paired phones, connects over Bluetooth Classic RFCOMM,
/// decodes the Opus frames and plays them. Reconnects automatically when the master goes away.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class ListenService : Service
{
    // Jitter buffer, in 20 ms Opus packets: start playing with 100 ms in hand.
    const int PrebufferFrames = 5;
    // Late packets pile up behind concealed ones; past this backlog we skip one to stay in sync.
    const int MaxBacklogFrames = PrebufferFrames + 3;
    const int MaxBufferedFrames = 15;
    // A hiccup is concealed by Opus; a longer silence means the link stalled, so we rebuild the cushion.
    const int MaxConcealedFrames = 10;
    static readonly TimeSpan LateGrace = TimeSpan.FromMilliseconds(10);
    const string LastMasterKey = "lastMasterAddress";

    CancellationTokenSource? cts;

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

        AuraNotifications.StartForeground(this, "Écoute en cours", ForegroundService.TypeMediaPlayback);

        var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
        if (adapter is not { IsEnabled: true })
        {
            AuraHub.Publish(new Failed("Active le Bluetooth pour écouter."));
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        cts = new CancellationTokenSource();
        var stoppingToken = cts.Token;
        _ = Task.Run(() => RunAsync(adapter, stoppingToken));
        return StartCommandResult.NotSticky;
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
                    await ListenAsync(socket, stoppingToken);
            }
            catch (System.OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is Java.IO.IOException or EndOfStreamException or InvalidDataException)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;
                if (ex is InvalidDataException)
                    AuraHub.Publish(new Failed(ex.Message));
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

    /// <summary>Tries each paired phone (last master first) until one accepts on the AuraCast service.</summary>
    static BluetoothSocket? ConnectToMaster(BluetoothAdapter adapter, CancellationToken stoppingToken)
    {
        var phones = adapter.BondedDevices?
            .Where(device => device.BluetoothClass?.MajorDeviceClass == MajorDeviceClass.Phone)
            .ToList() ?? [];
        if (phones.Count == 0)
            throw new InvalidOperationException("Appairez d'abord les deux téléphones dans les réglages Bluetooth.");

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

    static async Task ListenAsync(BluetoothSocket socket, CancellationToken stoppingToken)
    {
        using var closeOnStop = stoppingToken.Register(socket.Close);
        var stream = socket.InputStream!;
        AuraProtocol.ReadHeader(stream);
        AuraHub.Publish(new Listening(socket.RemoteDevice?.Name ?? "AuraCast"));
        await ReceiveAsync(stream, stoppingToken);
    }

    static async Task ReceiveAsync(System.IO.Stream stream, CancellationToken stoppingToken)
    {
        var stats = new ReceiveStats();
        var packets = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(MaxBufferedFrames) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        var playback = Task.Run(() => PlayAsync(packets.Reader, stats, stoppingToken), stoppingToken);

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
            await playback.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>
    /// Decodes on the playback clock, so a packet that is not there in time gets concealed by Opus
    /// instead of cutting the sound.
    /// </summary>
    static async Task PlayAsync(ChannelReader<byte[]> packets, ReceiveStats stats, CancellationToken stoppingToken)
    {
        int minBuffer = AudioTrack.GetMinBufferSize(AuraProtocol.SampleRate, ChannelOut.Stereo, AudioEncoding.Pcm16bit);
        using var track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)!
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(AudioEncoding.Pcm16bit)!
                .SetSampleRate(AuraProtocol.SampleRate)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!)!
            .SetTransferMode(AudioTrackMode.Stream)!
            .SetBufferSizeInBytes(minBuffer * 2)!
            .Build();

        var decoder = OpusCodecFactory.CreateDecoder(AuraProtocol.SampleRate, AuraProtocol.Channels);
        var pcm = new short[AuraProtocol.FrameSamples * AuraProtocol.Channels];

        track.Play();
        try
        {
            await Prebuffer(packets, stoppingToken);
            int concealedInARow = 0;
            while (!packets.Completion.IsCompleted)
            {
                if (!packets.TryRead(out var packet))
                {
                    // The AudioTrack still holds some audio: give a late packet a moment.
                    await Task.Delay(LateGrace, stoppingToken);
                    packets.TryRead(out packet);
                }

                if (packet is null)
                {
                    if (++concealedInARow > MaxConcealedFrames)
                    {
                        stats.Rebuffers++;
                        concealedInARow = 0;
                        await Prebuffer(packets, stoppingToken);
                        continue;
                    }
                    stats.Concealed++;
                    decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, AuraProtocol.FrameSamples);
                }
                else
                {
                    concealedInARow = 0;
                    decoder.Decode(packet, pcm, AuraProtocol.FrameSamples);
                    if (packets.Count > MaxBacklogFrames)
                    {
                        // Decoded (keeps the decoder state right) but not played: catch up 20 ms.
                        stats.Skipped++;
                        continue;
                    }
                }

                track.Write(pcm, 0, pcm.Length); // blocking: paces the loop to the playback clock
            }
        }
        finally
        {
            track.Stop();
            track.Release();
        }
    }

    static async Task Prebuffer(ChannelReader<byte[]> packets, CancellationToken stoppingToken)
    {
        while (packets.Count < PrebufferFrames && !packets.Completion.IsCompleted)
            await Task.Delay(5, stoppingToken);
    }

    sealed class ReceiveStats
    {
        long windowStart = Environment.TickCount64;
        int frames, bytes;
        public int Concealed, Skipped, Rebuffers, Dropped;

        public void Received(int packetBytes)
        {
            frames++;
            bytes += packetBytes + 2;
            long elapsed = Environment.TickCount64 - windowStart;
            if (elapsed < 5_000)
                return;

            // 50 frames/s means the link keeps up with real time.
            Log.Info(AuraLog.Tag, $"rx {frames * 1000.0 / elapsed:F1} frames/s, {bytes * 8.0 / elapsed:F0} kbps, "
                + $"concealed {Concealed}, skipped {Skipped}, rebuffers {Rebuffers}, dropped {Dropped}");
            windowStart = Environment.TickCount64;
            frames = bytes = Concealed = Skipped = Rebuffers = Dropped = 0;
        }
    }

    public override void OnDestroy()
    {
        cts?.Cancel();
        if (AuraHub.Current is not Failed) // keep the error on screen
            AuraHub.Publish(new Idle());
        base.OnDestroy();
    }
}
