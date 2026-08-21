using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeProduct710TurboCatalogTests
{
    [Fact]
    public void MatchesTheThreeSynapseDefaultTurboDefinitions()
    {
        Assert.Equal(
        [
            new BladeKeyboardMappingAction(0x2E, true, true),
            new BladeKeyboardMappingAction(0x2E, false, true),
        ], BladeProduct710TurboCatalog.Get(BladeProduct710TurboCatalog.VolumeDownId));

        Assert.Equal(
        [
            new BladeKeyboardMappingAction(0x30, true, true),
            new BladeKeyboardMappingAction(0x30, false, true),
        ], BladeProduct710TurboCatalog.Get(BladeProduct710TurboCatalog.VolumeUpId));

        Assert.Equal(
        [
            new BladeKeyboardMappingAction(0x5B, true, true),
            new BladeKeyboardMappingAction(0x19, true, false),
            new BladeDelayMappingAction(10),
            new BladeKeyboardMappingAction(0x19, false, false),
            new BladeKeyboardMappingAction(0x5B, false, true),
        ], BladeProduct710TurboCatalog.Get(BladeProduct710TurboCatalog.ProjectionSettingsId));
    }

    [Fact]
    public void RejectsUnknownTurboGuid()
    {
        Assert.False(BladeProduct710TurboCatalog.TryGet(Guid.NewGuid(), out var actions));
        Assert.Empty(actions);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeProduct710TurboCatalog.Get(Guid.NewGuid()));
    }
}
