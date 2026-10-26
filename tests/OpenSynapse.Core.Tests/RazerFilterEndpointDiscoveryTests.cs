using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerFilterEndpointDiscoveryTests
{
    private static readonly Guid BladeContainer =
        new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherContainer =
        new("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

    [Fact]
    public void SelectsBladeMi00InsteadOfFirstRazerEndpoint()
    {
        const string blade =
            @"\\?\RZCONTROL#VID_1532&PID_02C6&MI_00#BLADE#{E3BE005D-D130-4910-88FF-09AE02F680E9}";
        var candidates = new[]
        {
            Candidate(@"\\?\RZCONTROL#VID_1532&PID_00B8&MI_00#VIPER", OtherContainer),
            Candidate(@"\\?\RZCONTROL#VID_1532&PID_02C6&MI_01#WRONG-MI", BladeContainer),
            Candidate(@"\\?\RZCONTROL#VID_1532&PID_02C7&MI_00#WRONG-PID", BladeContainer),
            Candidate(blade, BladeContainer),
        };

        Assert.Equal(
            blade,
            RazerFilterEndpointDiscovery.SelectProduct710Endpoint(candidates));
        Assert.Equal(
            blade,
            RazerFilterEndpointDiscovery.SelectProduct710Endpoint(candidates, BladeContainer));
        Assert.Null(RazerFilterEndpointDiscovery.SelectProduct710Endpoint(
            candidates,
            OtherContainer));
    }

    [Fact]
    public void FailsClosedWhenProductEndpointIsAmbiguous()
    {
        var candidates = new[]
        {
            Candidate(@"\\?\RZCONTROL#VID_1532&PID_02C6&MI_00#ONE", BladeContainer),
            Candidate(@"\\?\rzcontrol#vid_1532&pid_02c6&mi_00#TWO", OtherContainer),
        };

        Assert.Null(RazerFilterEndpointDiscovery.SelectProduct710Endpoint(candidates));
        Assert.Equal(
            candidates[0].Path,
            RazerFilterEndpointDiscovery.SelectProduct710Endpoint(candidates, BladeContainer));
        Assert.Throws<ArgumentException>(() =>
            RazerFilterEndpointDiscovery.SelectProduct710Endpoint(candidates, Guid.Empty));
    }

    [Fact]
    public void RejectsMissingContainerIdentityAndDeduplicatesSamePath()
    {
        const string path = @"\\?\RZCONTROL#VID_1532&PID_02C6&MI_00#ONE";
        Assert.Null(RazerFilterEndpointDiscovery.SelectProduct710Endpoint(
        [
            Candidate(path, Guid.Empty),
        ]));
        Assert.Equal(
            path,
            RazerFilterEndpointDiscovery.SelectProduct710Endpoint(
            [
                Candidate(path, BladeContainer),
                Candidate(path.ToLowerInvariant(), BladeContainer),
            ],
            BladeContainer));
    }

    [Fact]
    public void SelectsArgbControllerMi00ByProductId()
    {
        const string argb =
            @"\\?\RZCONTROL#VID_1532&PID_0F1F&MI_00#ARGB#{E3BE005D-D130-4910-88FF-09AE02F680E9}";
        var candidates = new[]
        {
            Candidate(@"\\?\RZCONTROL#VID_1532&PID_02C6&MI_00#BLADE", BladeContainer),
            Candidate(@"\\?\RZCONTROL#VID_1532&PID_0F1F&MI_01#WRONG-MI", OtherContainer),
            Candidate(argb, OtherContainer),
        };

        Assert.Equal(argb, RazerFilterEndpointDiscovery.SelectEndpoint(candidates, 0x0F1F));
        Assert.Null(RazerFilterEndpointDiscovery.SelectEndpoint(candidates, 0x0F1E));
    }

    private static RazerFilterEndpointCandidate Candidate(string path, Guid containerId) =>
        new(path, containerId);
}
