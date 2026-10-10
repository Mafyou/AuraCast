namespace AuraMusic.Mobile.Casting;

/// <summary>Runtime permissions for Bluetooth and Wi-Fi streaming; the master additionally needs the microphone permission for playback capture.</summary>
sealed class AuraPermissions(bool capture) : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
    {
        get
        {
            // Before Android 12, Bluetooth on paired devices only needs install-time permissions.
            List<(string, bool)> permissions = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? [(Manifest.Permission.BluetoothConnect, true)]
                : [];

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                permissions.Add((Manifest.Permission.PostNotifications, true));
            if (capture)
                permissions.Add((Manifest.Permission.RecordAudio, true));

            return [.. permissions];
        }
    }
}

/// <summary>
/// Newer Android versions gate the local network behind "nearby Wi-Fi devices". Asked on its own and optional:
/// refused, only the Wi-Fi link is lost, Bluetooth still works.
/// </summary>
sealed class WifiPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33) ? [(Manifest.Permission.NearbyWifiDevices, true)] : [];
}
