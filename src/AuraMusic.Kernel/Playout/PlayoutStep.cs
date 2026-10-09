namespace AuraMusic.Kernel.Playout;

/// <summary>The packet arrived in time: decode it and play it.</summary>
public sealed record Play(byte[] Packet);

/// <summary>We are slightly behind: play the packet a little shorter (see <see cref="PcmCrossfade"/>) to catch up unheard.</summary>
public sealed record CatchUp(byte[] Packet);

/// <summary>The packet is late: let the decoder conceal the gap.</summary>
public sealed record Conceal;

/// <summary>We are far behind: decode the packet to keep the decoder state right, but do not play it.</summary>
public sealed record Skip(byte[] Packet);

/// <summary>The link stalled: stop concealing and wait for a new cushion of packets.</summary>
public sealed record Rebuffer;

public union PlayoutStep(Play, CatchUp, Conceal, Skip, Rebuffer);
