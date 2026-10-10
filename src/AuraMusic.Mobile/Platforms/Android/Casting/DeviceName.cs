namespace AuraMusic.Mobile.Casting;

static class DeviceName
{
    const string IdKey = "deviceId";

    /// <summary>
    /// Created once per installation: lets a master recognise this phone over Bluetooth and over Wi-Fi alike.
    /// </summary>
    public static Guid Id
    {
        get
        {
            if (field == Guid.Empty && !Guid.TryParse(Preferences.Get(IdKey, null), out field))
                Preferences.Set(IdKey, (field = Guid.NewGuid()).ToString("N"));
            return field;
        }
    }

    /// <summary>The name the user gave the phone (the one Bluetooth shows too), else its model.</summary>
    public static string Of(Context context) =>
        global::Android.Provider.Settings.Global.GetString(context.ContentResolver, global::Android.Provider.Settings.Global.DeviceName)
        ?? Build.Model
        ?? "AuraMusic";
}
