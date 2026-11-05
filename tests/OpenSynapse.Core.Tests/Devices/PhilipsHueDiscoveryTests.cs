using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class PhilipsHueDiscoveryTests
{
    [Fact]
    public void SsdpReplyIsParsedOnlyForHueBridges()
    {
        const string reply =
            "HTTP/1.1 200 OK\r\n" +
            "LOCATION: http://192.168.1.22:80/description.xml\r\n" +
            "SERVER: Linux/3.14.0 IpBridge/1.60.1\r\n" +
            "ST: upnp:rootdevice\r\n" +
            "USN: uuid:2f402f80-da50-11e1-9b23-001788fffe123456::upnp:rootdevice\r\n" +
            "hue-bridgeid: 001788FFFE123456\r\n\r\n";

        var bridge = PhilipsHueDiscovery.ParseSsdpResponse(reply);

        Assert.NotNull(bridge);
        Assert.Equal("192.168.1.22", bridge.Host);
        Assert.Equal("001788FFFE123456", bridge.Id);
        Assert.Equal("1.60.1", bridge.SoftwareVersion);
    }

    [Fact]
    public void NonHueSsdpReplyIsIgnored()
    {
        const string reply =
            "HTTP/1.1 200 OK\r\n" +
            "LOCATION: http://192.168.1.40:80/description.xml\r\n" +
            "SERVER: Linux/4.0 UPnP/1.0 OtherDevice/2.0\r\n" +
            "ST: upnp:rootdevice\r\n" +
            "USN: uuid:not-a-hue-bridge::upnp:rootdevice\r\n\r\n";

        Assert.Null(PhilipsHueDiscovery.ParseSsdpResponse(reply));
    }

    [Fact]
    public void NupnpReplyKeepsOnlyIPv4Bridges()
    {
        const string json =
            """
            [
              {"id":"001788FFFE123456","internalipaddress":"192.168.1.22"},
              {"id":"001788FFFE654321","internalipaddress":"fe80::1234"},
              {"id":"001788FFFE000000","internalipaddress":123},
              {"id":"bad","internalipaddress":"not-an-address"}
            ]
            """;

        var bridges = PhilipsHueDiscovery.ParseNupnpResponse(json);

        Assert.Single(bridges);
        Assert.Equal("192.168.1.22", bridges[0].Host);
        Assert.Equal("001788FFFE123456", bridges[0].Id);
    }

    [Fact]
    public void NupnpReplyAcceptsTheWrappedDataShape()
    {
        const string json =
            """{"data":[{"id":"001788FFFE123456","internalipaddress":"10.0.0.2"}]}""";

        var bridge = Assert.Single(PhilipsHueDiscovery.ParseNupnpResponse(json));
        Assert.Equal("10.0.0.2", bridge.Host);
    }
}
