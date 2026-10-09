using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Projection;

namespace AuraCast.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        const int MediaProjectionRequestCode = 4242;

        TaskCompletionSource<(Result ResultCode, Intent? Data)>? mediaProjectionRequest;

        /// <summary>Shows the system "start recording or casting" consent dialog required for playback capture.</summary>
        public Task<(Result ResultCode, Intent? Data)> RequestMediaProjectionAsync()
        {
            var manager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
            // Android 14+: go straight to the whole-device consent instead of asking to pick an app.
            var intent = OperatingSystem.IsAndroidVersionAtLeast(34)
                ? manager.CreateScreenCaptureIntent(MediaProjectionConfig.CreateConfigForDefaultDisplay())
                : manager.CreateScreenCaptureIntent();

            mediaProjectionRequest?.TrySetCanceled();
            mediaProjectionRequest = new();
            StartActivityForResult(intent, MediaProjectionRequestCode);
            return mediaProjectionRequest.Task;
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == MediaProjectionRequestCode)
                mediaProjectionRequest?.TrySetResult((resultCode, data));
        }
    }
}
