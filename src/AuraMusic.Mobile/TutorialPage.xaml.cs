using System.Collections.Immutable;
using AuraMusic.Mobile.Resources.Strings;

namespace AuraMusic.Mobile
{
    public sealed record TutorialStep(string Emoji, string Title, string Text);

    public partial class TutorialPage : ContentPage
    {
        const string SeenKey = "tutorialSeen";

        static readonly ImmutableArray<TutorialStep> All =
        [
            new("⚡", AppStrings.TutorialWelcomeTitle, AppStrings.TutorialWelcomeText),
            new("🔗", AppStrings.TutorialPairTitle, AppStrings.TutorialPairText),
            new("📡", AppStrings.TutorialBroadcastTitle, AppStrings.TutorialBroadcastText),
            new("🎧", AppStrings.TutorialListenTitle, AppStrings.TutorialListenText),
            new("💡", AppStrings.TutorialTipsTitle, AppStrings.TutorialTipsText),
        ];

        public TutorialPage()
        {
            InitializeComponent();
            Steps.ItemsSource = All;
        }

        public static bool HasBeenSeen => Preferences.Get(SeenKey, false);

        void OnPositionChanged(object? sender, PositionChangedEventArgs e) =>
            NextButton.Text = e.CurrentPosition == All.Length - 1 ? AppStrings.TutorialDone : AppStrings.TutorialNext;

        async void OnNextClicked(object? sender, EventArgs e)
        {
            if (Steps.Position < All.Length - 1)
                Steps.Position++;
            else
                await CloseAsync();
        }

        async void OnDoneClicked(object? sender, EventArgs e) => await CloseAsync();

        async Task CloseAsync()
        {
            Preferences.Set(SeenKey, true);
            await Navigation.PopModalAsync();
        }
    }
}
