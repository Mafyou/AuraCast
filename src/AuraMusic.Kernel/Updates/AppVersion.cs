namespace AuraMusic.Kernel.Updates;

public static class AppVersion
{
    /// <summary>
    /// Reads "1.2", "1.2.0" or a tag like "v1.2.0" as a three-part version, so that 1.2 and 1.2.0 compare equal.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text) || !Version.TryParse(text.Trim().TrimStart('v', 'V'), out var parsed))
            return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }
}
