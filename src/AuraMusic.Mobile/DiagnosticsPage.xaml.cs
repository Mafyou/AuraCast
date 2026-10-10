namespace AuraMusic.Mobile
{
    /// <summary>What the app is doing right now, in figures. Opened by double-tapping the artwork of the home screen.</summary>
    public partial class DiagnosticsPage : ContentPage
    {
        const int LabelWidth = 22;

        public DiagnosticsPage()
        {
            InitializeComponent();
            Render();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            DiagnosticsHub.Updated += OnUpdated;
            AuraHub.StateChanged += OnStateChanged;
            Render();
        }

        protected override void OnDisappearing()
        {
            DiagnosticsHub.Updated -= OnUpdated;
            AuraHub.StateChanged -= OnStateChanged;
            base.OnDisappearing();
        }

        void OnUpdated(DiagnosticsReport report) => Dispatcher.Dispatch(Render);

        void OnStateChanged(AuraState state) => Dispatcher.Dispatch(Render);

        void Render()
        {
            var text = new StringBuilder();
            void Line(string label, string value) => text.Append(label.PadRight(LabelWidth)).AppendLine(value);

            Line("app", $"AuraMusic {AppInfo.Current.VersionString} ({LanguageSettings.Current})");
            Line("phone", $"{DeviceName.Of(Platform.AppContext)} · {DeviceName.Id.ToString("N")[..8]}");
            Line("state", DescribeState(AuraHub.Current));
            Line("sync target", $"{PlayoutTuning.TargetLatencyMs} ms");
            text.AppendLine();

            var lines = DiagnosticsText.Lines(DiagnosticsHub.Current);
            if (lines.IsEmpty)
                text.Append(AppStrings.DiagnosticsIdle);
            foreach (var (label, value) in lines)
                Line(label, value);

            Report.Text = text.ToString();
        }

        static string DescribeState(AuraState state) => state switch
        {
            Idle => "idle",
            Advertising => "sharing, nobody listening",
            Streaming(var listeners) => $"sharing to {string.Join(", ", listeners)}",
            Searching(var problem) => problem is null ? "searching" : $"searching ({problem})",
            Listening(var master, var links) => $"listening to {master} via {DiagnosticsText.Describe(links)}",
            Failed(var reason) => $"failed: {reason}",
        };

        async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
    }
}
