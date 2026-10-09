using AuraCast.Kernel.State;
using AuraCast.Mobile.Casting;

namespace AuraCast.Mobile
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            Render(AuraHub.Current);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            AuraHub.StateChanged += OnStateChanged;
            Render(AuraHub.Current);
        }

        protected override void OnDisappearing()
        {
            AuraHub.StateChanged -= OnStateChanged;
            base.OnDisappearing();
        }

        void OnStateChanged(AuraState state) => MainThread.BeginInvokeOnMainThread(() => Render(state));

        void Render(AuraState state)
        {
            (StatusLabel.Text, HintLabel.Text) = state switch
            {
                Idle => ("Prêt", "Diffuse le son de ton téléphone, ou écoute celui de l'autre."),
                Advertising => ("En attente d'une oreille…", "Sur l'autre téléphone, appuie sur « Écouter »."),
                Streaming(var listeners) => (listeners == 1 ? "1 personne t'écoute 💙" : $"{listeners} personnes t'écoutent 💙",
                    "Lance YouTube Music ou n'importe quelle app : tout le son est partagé."),
                Searching => ("Recherche du téléphone qui diffuse…", "Les deux téléphones doivent être appairés en Bluetooth et à quelques mètres l'un de l'autre."),
                Listening(var master) => ($"À l'écoute de {master} 🎧", "Tu peux éteindre l'écran, la musique continue."),
                Failed(var reason) => ("Oups", reason),
            };

            bool active = state is Advertising or Streaming or Searching or Listening;
            ModeButtons.IsVisible = !active;
            StopButton.IsVisible = active;
        }

        async void OnBroadcastClicked(object? sender, EventArgs e) => await RunAsync(AuraController.StartBroadcastAsync);

        async void OnListenClicked(object? sender, EventArgs e) => await RunAsync(AuraController.StartListeningAsync);

        void OnStopClicked(object? sender, EventArgs e) => AuraController.Stop();

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
