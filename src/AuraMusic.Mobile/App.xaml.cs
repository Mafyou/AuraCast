namespace AuraMusic.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            LanguageSettings.Apply();
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}