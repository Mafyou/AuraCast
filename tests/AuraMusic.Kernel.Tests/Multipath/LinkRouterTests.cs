namespace AuraMusic.Kernel.Tests.Multipath;

public sealed class LinkRouterTests
{
    static readonly Guid Nord2 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    static readonly Guid OnePlus7 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    static (bool[] Carries, bool AllOnWifi) Plan(params LinkStatus[] links)
    {
        var carries = new bool[links.Length];
        bool allOnWifi = LinkRouter.Plan(links, carries);
        return (carries, allOnWifi);
    }

    [Fact]
    public void BluetoothOnly_CarriesAtTheBluetoothBitrate()
    {
        var (carries, allOnWifi) = Plan(new LinkStatus(Nord2, Links.Bluetooth, false));

        carries.ShouldBe([true]);
        allOnWifi.ShouldBeFalse();
    }

    [Fact]
    public void HealthyWifi_PutsTheSamePhonesBluetoothToRest()
    {
        var (carries, allOnWifi) = Plan(
            new LinkStatus(Nord2, Links.Bluetooth, false),
            new LinkStatus(Nord2, Links.Wifi, true));

        carries.ShouldBe([false, true]);
        allOnWifi.ShouldBeTrue();
    }

    [Fact]
    public void WifiFallingBehind_WakesBluetoothAtOnce()
    {
        var (carries, allOnWifi) = Plan(
            new LinkStatus(Nord2, Links.Bluetooth, false),
            new LinkStatus(Nord2, Links.Wifi, false)); // just connected, or its queue is backing up

        carries.ShouldBe([true, true]);
        allOnWifi.ShouldBeFalse();
    }

    [Fact]
    public void OnePhoneOnWifi_DoesNotRestAnotherPhonesBluetooth()
    {
        var (carries, allOnWifi) = Plan(
            new LinkStatus(Nord2, Links.Wifi, true),
            new LinkStatus(OnePlus7, Links.Bluetooth, false));

        carries.ShouldBe([true, true]);
        allOnWifi.ShouldBeFalse(); // the Bluetooth-only phone keeps everyone at the safe bitrate
    }

    [Fact]
    public void EveryPhoneOnHealthyWifi_AllowsTheHigherBitrate()
    {
        var (carries, allOnWifi) = Plan(
            new LinkStatus(Nord2, Links.Wifi, true),
            new LinkStatus(OnePlus7, Links.Wifi, true),
            new LinkStatus(OnePlus7, Links.Bluetooth, false));

        carries.ShouldBe([true, true, false]);
        allOnWifi.ShouldBeTrue();
    }

    [Fact]
    public void NoListener_IsNotAllOnWifi()
    {
        Plan().AllOnWifi.ShouldBeFalse();
    }
}
