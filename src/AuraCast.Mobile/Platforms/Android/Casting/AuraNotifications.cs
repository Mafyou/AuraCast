using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;

namespace AuraCast.Mobile.Casting;

static class AuraNotifications
{
    const string ChannelId = "auracast";
    const int NotificationId = 1;
    public const string ActionStop = "com.mafyou.auracast.STOP";

    public static void StartForeground(Service service, string text, ForegroundService type)
    {
        var manager = (NotificationManager)service.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "AuraCast", NotificationImportance.Low));

        var open = PendingIntent.GetActivity(service, 0, new Intent(service, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var stop = PendingIntent.GetService(service, 0, new Intent(service, service.GetType()).SetAction(ActionStop), PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(service, ChannelId)
            .SetContentTitle("AuraCast")
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetOngoing(true)
            .SetContentIntent(open)
            .AddAction(new Notification.Action.Builder(
                Icon.CreateWithResource(service, global::Android.Resource.Drawable.IcMediaPause), "Arrêter", stop).Build())
            .Build();

        service.StartForeground(NotificationId, notification, type);
    }
}
