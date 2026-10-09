namespace AuraMusic.Kernel.Updates;

/// <summary>A published version of the app and where to download its APK.</summary>
public sealed record AppRelease(Version Version, string Tag, Uri ApkUrl);
