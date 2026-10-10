namespace AuraMusic.Mobile.Casting;

static class DeviceName
{
    /// <summary>The name the user gave the phone (the one Bluetooth shows too), else its model.</summary>
    public static string Of(Context context) =>
        global::Android.Provider.Settings.Global.GetString(context.ContentResolver, global::Android.Provider.Settings.Global.DeviceName)
        ?? Build.Model
        ?? "AuraMusic";
}
