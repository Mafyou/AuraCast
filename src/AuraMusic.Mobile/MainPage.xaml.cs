namespace AuraMusic.Mobile
{
    public partial class MainPage : ContentPage
    {
        // Only on launch, not when the page is rebuilt after a language switch.
        static bool splashShown, updateOffered;

        public MainPage()
        {
            InitializeComponent();
            LanguageButton.Text = $"🌐 {AppLanguages.Next(LanguageSettings.Current).ToUpperInvariant()}";
            Render(AuraHub.Current);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            AuraHub.StateChanged += OnStateChanged;
            SpectrumHub.Updated += OnSpectrum;
            Render(AuraHub.Current);

            if (!splashShown)
            {
                splashShown = true;
                await Task.Delay(TimeSpan.FromSeconds(2));
                await SplashOverlay.FadeToAsync(0, 400);
            }
            SplashOverlay.IsVisible = false;

            if (!TutorialPage.HasBeenSeen && Navigation.ModalStack.Count == 0)
                await Navigation.PushModalAsync(new TutorialPage());
            else if (!updateOffered)
            {
                updateOffered = true;
                await OfferUpdateAsync();
            }
        }

        protected override void OnDisappearing()
        {
            AuraHub.StateChanged -= OnStateChanged;
            SpectrumHub.Updated -= OnSpectrum;
            base.OnDisappearing();
        }

        void OnStateChanged(AuraState state) => MainThread.BeginInvokeOnMainThread(() => Render(state));

        void OnSpectrum(float[] levels) => Spectrum.Post(levels);

        void Render(AuraState state)
        {
            (StatusLabel.Text, HintLabel.Text) = state switch
            {
                Idle => (AppStrings.StatusIdle, AppStrings.HintIdle),
                Advertising => (AppStrings.StatusAdvertising, AppStrings.HintAdvertising),
                Streaming(1) => (AppStrings.StatusStreamingOne, AppStrings.HintStreaming),
                Streaming(var listeners) => (Format(AppStrings.StatusStreamingMany, listeners), AppStrings.HintStreaming),
                Searching => (AppStrings.StatusSearching, AppStrings.HintSearching),
                Listening(var master) => (Format(AppStrings.StatusListening, master), AppStrings.HintListening),
                Failed(var reason) => (AppStrings.StatusFailed, reason),
            };

            bool active = state is Advertising or Streaming or Searching or Listening;
            ModeButtons.IsVisible = !active;
            ActivePanel.IsVisible = active;
            if (!active)
                Spectrum.Clear();
        }

        async void OnBroadcastClicked(object? sender, EventArgs e) => await RunAsync(AuraController.StartBroadcastAsync);

        async void OnListenClicked(object? sender, EventArgs e) => await RunAsync(AuraController.StartListeningAsync);

        void OnStopClicked(object? sender, EventArgs e) => AuraController.Stop();

        static string Format(string format, object value) => string.Format(CultureInfo.CurrentCulture, format, value);

        async Task OfferUpdateAsync()
        {
            if (await AppUpdater.CheckAsync(CancellationToken.None) is not UpdateAvailable(var release))
                return; // up to date, or offline: we will look again next launch
            if (!await MatrixDialog.ShowAsync(this, AppStrings.UpdateTitle, Format(AppStrings.UpdateMessage, release.Version.ToString(3)),
                    AppStrings.UpdateNow, AppStrings.UpdateLater))
                return;

            var progress = new Progress<double>(done => StatusLabel.Text = Format(AppStrings.UpdateDownloading, done));
            try
            {
                if (!await AppUpdater.DownloadAndInstallAsync(release, progress, CancellationToken.None))
                    await MatrixDialog.ShowAsync(this, AppStrings.UpdateTitle, AppStrings.UpdateAllowInstall, "OK");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                await MatrixDialog.ShowAsync(this, AppStrings.UpdateTitle, Format(AppStrings.UpdateFailed, ex.Message), "OK");
            }
            finally
            {
                Render(AuraHub.Current);
            }
        }

        void OnLanguageClicked(object? sender, EventArgs e)
        {
            LanguageSettings.SwitchToNext();
            // Rebuild the UI so every text is looked up again in the new language.
            Window.Page = new AppShell();
        }

        async void OnHelpClicked(object? sender, EventArgs e) => await Navigation.PushModalAsync(new TutorialPage());

        static async Task RunAsync(Func<Task> start)
        {
            try
            {
                await start();
            }
            catch (Exception ex)
            {
                AuraHub.Publish(new Failed(ex.Message));
            }
        }
    }
}
