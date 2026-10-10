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
            AuraHub.Publish(new Streaming(["Nord 2", "OnePlus 7 Pro"]));

            (AuraHub.Current is Streaming { Count: 2 }).ShouldBeTrue();
            (received is Streaming { Count: 2 }).ShouldBeTrue();
        }
        finally
        {
            AuraHub.StateChanged -= OnChanged;
            AuraHub.Publish(new Idle());
        }
    }

    [Fact]
    public void Streaming_KeepsTheListenersNames()
    {
        var streaming = new Streaming(["Nord 2", "OnePlus 7 Pro"]);

        streaming.Count.ShouldBe(2);
        streaming.Listeners.ShouldBe(["Nord 2", "OnePlus 7 Pro"]);
    }

    [Theory]
    [InlineData(new[] { "Nord 2" }, "1 : Nord 2")]
    [InlineData(new[] { "Nord 2", "OnePlus 7 Pro" }, "2 : Nord 2, OnePlus 7 Pro")]
    public void States_PatternMatchExhaustively(string[] names, string expected)
    {
        AuraState state = new Streaming([.. names]);

        var text = state switch
        {
            Idle or Advertising or Searching => "",
            Streaming(var listeners) => $"{listeners.Length} : {string.Join(", ", listeners)}",
            Listening(var master) => master,
            Failed(var reason) => reason,
        };

        text.ShouldBe(expected);
    }
}
