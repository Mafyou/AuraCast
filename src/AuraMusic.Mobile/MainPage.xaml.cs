namespace AuraMusic.Mobile
{
    public partial class MainPage : ContentPage
    {
        // Only on launch, not when the page is rebuilt after a language switch.
        static bool splashShown, updateOffered, updating;

        public MainPage()
        {
            InitializeComponent();
            // Inside the pull-to-update ScrollView the layout would collapse: give it the visible height.
            HomeScroll.SizeChanged += (_, _) => HomeLayout.HeightRequest = HomeScroll.Height;
            LanguageButton.Text = $"🌐 {AppLanguages.Next(LanguageSettings.Current).ToUpperInvariant()}";
            VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
#if DEBUG
            // A test build shows the version being worked on, not a published one, and never updates itself.
            VersionLabel.Text += " · dev";
#endif
            // Maximum first: a slider refuses a minimum above its current maximum of 1.
            SyncSlider.Maximum = PlayoutTuning.MaxLatencyMs;
            SyncSlider.Minimum = PlayoutTuning.MinLatencyMs;
            SyncSlider.Value = PlayoutTuning.TargetLatencyMs;
            ShowSync(PlayoutTuning.TargetLatencyMs);
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
                Streaming(var names) => (names.Length == 1 ? AppStrings.StatusStreamingOne : Format(AppStrings.StatusStreamingMany, names.Length),
                    Format(AppStrings.HintStreamingWith, string.Join(", ", names))),
                Searching(var problem) => (AppStrings.StatusSearching, problem ?? AppStrings.HintSearching),
                Listening(var master, var links) => (Format(AppStrings.StatusListening, master), Format(AppStrings.HintListeningVia, Describe(links))),
                Failed(var reason) => (AppStrings.StatusFailed, reason),
            };

            bool active = state is Advertising or Streaming or Searching or Listening;
            ModeButtons.IsVisible = !active;
            ActivePanel.IsVisible = active;
            SyncPanel.IsVisible = state is Searching or Listening;
            if (!active)
                Spectrum.Clear();
        }

        void OnSyncChanged(object? sender, ValueChangedEventArgs e)
        {
            // Whole packets only: the jitter buffer counts in 20 ms steps.
            int latency = (int)Math.Round(e.NewValue / PlayoutTuning.FrameMs) * PlayoutTuning.FrameMs;
            if (latency == PlayoutTuning.TargetLatencyMs)
                return;
            PlayoutTuning.TargetLatencyMs = latency; // picked up by the playback thread on its next packet
            Preferences.Set(App.SyncLatencyKey, PlayoutTuning.TargetLatencyMs);
            ShowSync(PlayoutTuning.TargetLatencyMs);
        }

        void ShowSync(int latency) => SyncLabel.Text = Format(AppStrings.SyncDelay, latency);

        async void OnArtworkDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (Navigation.ModalStack.Count == 0)
                await Navigation.PushModalAsync(new DiagnosticsPage());
        }

        async void OnBroadcastClicked(object? sender, EventArgs e) => await RunAsync(AuraController.StartBroadcastAsync);

        async void OnListenClicked(object? sender, EventArgs e) => await RunAsync(AuraController.StartListeningAsync);

        void OnStopClicked(object? sender, EventArgs e) => AuraController.Stop();

        static string Format(string format, object value) => string.Format(CultureInfo.CurrentCulture, format, value);

        static string Describe(Links links) => links switch
        {
            Links.Wifi | Links.Bluetooth => "Wi-Fi + Bluetooth",
            Links.Wifi => "Wi-Fi",
            _ => "Bluetooth",
        };

        async void OnPullToUpdate(object? sender, EventArgs e)
        {
            try
            {
                if (!updating)
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
            updating = true;
            try
            {
                await CheckAndInstallAsync(onDemand);
            }
            finally
            {
                updating = false;
            }
        }

        async Task CheckAndInstallAsync(bool onDemand)
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
            catch (Exception ex) when (ex is HttpRequestException or IOException or System.OperationCanceledException or Java.Lang.Exception)
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
