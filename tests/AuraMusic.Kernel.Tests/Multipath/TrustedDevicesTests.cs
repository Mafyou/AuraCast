namespace AuraMusic.Kernel.Tests.Multipath;

public sealed class TrustedDevicesTests
{
    static readonly Guid Nord2 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    static readonly Guid OnePlus7 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Empty_TrustsNobody()
    {
        TrustedDevices.Empty.Contains(Nord2).ShouldBeFalse();
        TrustedDevices.Empty.Count.ShouldBe(0);
    }

    [Fact]
    public void With_TrustsThatPhoneOnly()
    {
        var trusted = TrustedDevices.Empty.With(Nord2);

        trusted.Contains(Nord2).ShouldBeTrue();
        trusted.Contains(OnePlus7).ShouldBeFalse();
    }

    [Fact]
    public void With_AlreadyTrusted_ReturnsTheSameInstance()
    {
        var trusted = TrustedDevices.Empty.With(Nord2);

        trusted.With(Nord2).ShouldBeSameAs(trusted); // nothing to save again
    }

    [Fact]
    public void SerializeThenParse_RoundTrips()
    {
        var trusted = TrustedDevices.Empty.With(Nord2).With(OnePlus7);

        var restored = TrustedDevices.Parse(trusted.Serialize());

        restored.Count.ShouldBe(2);
        restored.Contains(Nord2).ShouldBeTrue();
        restored.Contains(OnePlus7).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    public void Parse_NothingStored_IsEmpty(string? stored)
    {
        TrustedDevices.Parse(stored).Count.ShouldBe(0);
    }

    [Fact]
    public void Parse_SkipsUnreadableEntries()
    {
        var restored = TrustedDevices.Parse($"oops, {Nord2:N} ,42");

        restored.Count.ShouldBe(1);
        restored.Contains(Nord2).ShouldBeTrue();
    }
}
