namespace AuraMusic.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            LanguageSettings.Apply();
            InitializeComponent();
        }

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