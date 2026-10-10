namespace AuraMusic.Kernel.Tests.Spectrum;

public sealed class SpectrumHubTests
{
    [Fact]
    public void IsObserved_FollowsSubscriptions()
    {
        void OnUpdated(float[] _) { }

        SpectrumHub.IsObserved.ShouldBeFalse();
        SpectrumHub.Updated += OnUpdated;
        try
        {
            SpectrumHub.IsObserved.ShouldBeTrue();
        }
        finally
        {
            SpectrumHub.Updated -= OnUpdated;
        }
        SpectrumHub.IsObserved.ShouldBeFalse();
    }

    [Fact]
    public void Publish_ReachesSubscribers()
    {
        float[]? received = null;
        void OnUpdated(float[] levels) => received = levels;
        var levels = new float[SpectrumHub.Bands];

        SpectrumHub.Updated += OnUpdated;
        try
        {
            SpectrumHub.Publish(levels);
        }
        finally
        {
            SpectrumHub.Updated -= OnUpdated;
        }

        received.ShouldBeSameAs(levels);
    }
}
