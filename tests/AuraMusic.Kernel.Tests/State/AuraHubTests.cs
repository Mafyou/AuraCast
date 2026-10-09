using AuraMusic.Kernel.State;
using Shouldly;

namespace AuraMusic.Kernel.Tests.State;

public sealed class AuraHubTests
{
    [Fact]
    public void Publish_UpdatesCurrentAndNotifies()
    {
        AuraState? received = null;
        void OnChanged(AuraState state) => received = state;
        AuraHub.StateChanged += OnChanged;
        try
        {
            AuraHub.Publish(new Streaming(2));

            (AuraHub.Current is Streaming { Listeners: 2 }).ShouldBeTrue();
            (received is Streaming { Listeners: 2 }).ShouldBeTrue();
        }
        finally
        {
            AuraHub.StateChanged -= OnChanged;
            AuraHub.Publish(new Idle());
        }
    }

    [Theory]
    [InlineData(1, "1 personne")]
    [InlineData(3, "3 personnes")]
    public void States_PatternMatchExhaustively(int listeners, string expected)
    {
        AuraState state = new Streaming(listeners);

        var text = state switch
        {
            Idle or Advertising or Searching => "",
            Streaming(1) => "1 personne",
            Streaming(var count) => $"{count} personnes",
            Listening(var master) => master,
            Failed(var reason) => reason,
        };

        text.ShouldBe(expected);
    }
}
