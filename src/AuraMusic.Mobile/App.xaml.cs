namespace AuraMusic.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            LanguageSettings.Apply();
            // The sync slider's last position, before any listening starts.
            PlayoutTuning.TargetLatencyMs = Preferences.Get(SyncLatencyKey, PlayoutTuning.DefaultLatencyMs);
            InitializeComponent();
        }

        /// <summary>Where the listener's chosen delay is kept between launches.</summary>
        public const string SyncLatencyKey = "syncLatencyMs";

        protected override void OnResume()
        {
            base.OnResume();
            // Back from Android's "install unknown apps" switch: start the update that was waiting for it.
            AppUpdater.ResumePendingInstall();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}