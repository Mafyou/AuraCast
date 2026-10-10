namespace AuraMusic.Mobile
{
    public partial class MainPage : ContentPage
    {
        // Only on launch, not when the page is rebuilt after a language switch.
        static bool splashShown, updateOffered;

        public MainPage()
        {
            InitializeComponent();
            // Inside the pull-to-update ScrollView the layout would collapse: give it the visible height.
            HomeScroll.SizeChanged += (_, _) => HomeLayout.HeightRequest = HomeScroll.Height;
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
            {
                var tutorial = new TutorialPage();
                await Navigation.PushModalAsync(tutorial);
                await tutorial.Closed; // a first launch must be offered updates too, once the tutorial is done
            }

            if (!updateOffered)
            {
                updateOffered = true;
                await OfferUpdateAsync(onDemand: false);
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

        async void OnPullToUpdate(object? sender, EventArgs e)
        {
            try
            {
                await OfferUpdateAsync(onDemand: true);
            }
            finally
            {
                UpdateRefresh.IsRefreshing = false;
            }
        }

        /// <param name="onDemand">Pulled by the user: say so when there is nothing new, instead of staying silent.</param>
        async Task OfferUpdateAsync(bool onDemand)
        {
            var check = await AppUpdater.CheckAsync(CancellationToken.None);
            if (check is not UpdateAvailable(var release))
            {
                if (onDemand)
                {
                    var message = check is CheckFailed(var reason)
                        ? Format(AppStrings.UpdateCheckFailed, reason)
                        : Format(AppStrings.UpdateUpToDate, AppInfo.Current.VersionString);
                    await MatrixDialog.ShowAsync(this, AppStrings.UpdateCheckTitle, message, "OK");
                }
                return; // at launch, stay silent: up to date, or offline and we will look again next time
            }
            if (!await MatrixDialog.ShowAsync(this, AppStrings.UpdateTitle, Format(AppStrings.UpdateMessage, release.Version.ToString(3)),
                    AppStrings.UpdateNow, AppStrings.UpdateLater))
                return;

            var progress = new Progress<double>(done => StatusLabel.Text = Format(AppStrings.UpdateDownloading, done));
            try
            {
                var apk = await AppUpdater.DownloadAsync(release, progress, CancellationToken.None);
                if (AppUpdater.CanInstall)
                    AppUpdater.Install(apk);
                else if (await MatrixDialog.ShowAsync(this, AppStrings.UpdateTitle, AppStrings.UpdateAllowInstall, AppStrings.UpdateOpenSettings))
                    AppUpdater.InstallWhenAllowed(apk); // resumed by App.OnResume when the user comes back
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or Java.Lang.Exception)
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
