using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ProfileExtendedSettingsTests
{
    private static readonly DeviceDescriptor Blade = new(
        "blade", "Blade", 0x1532, 0x02C6, DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation, 91, 1, 2, "blade-710");

    [Fact]
    public void CloneDeepCopiesShortcutAndViperCollections()
    {
        var document = ProfileDocument.CreateDefault();
        var active = document.Profiles[document.ActiveProfileName];
        active.Shortcuts.PerformanceCycleModes = [BladePerformanceMode.Balanced];
        active.Shortcuts.RefreshRateCycleHertz = [240];
        document.Global.Viper.ButtonAssignments = Assignments(ViperButtonMappingFunction.MouseButton);

        var clone = document.Clone();
        active.Shortcuts.PerformanceCycleModes[0] = BladePerformanceMode.Performance;
        active.Shortcuts.RefreshRateCycleHertz[0] = 60;
        document.Global.Viper.ButtonAssignments[0].FunctionData[0] = 2;

        var clonedActive = clone.Profiles[clone.ActiveProfileName];
        Assert.Equal(BladePerformanceMode.Balanced, clonedActive.Shortcuts.PerformanceCycleModes![0]);
        Assert.Equal(240, clonedActive.Shortcuts.RefreshRateCycleHertz![0]);
        Assert.Equal(1, clone.Global.Viper.ButtonAssignments![0].FunctionData[0]);
    }

    [Fact]
    public void ResolverUsesAtomicOverridePrecedenceForNewSettings()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.SnapTapEnabled = false;
        document.Global.Blade.MappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
        document.Global.Viper.ButtonAssignments = Assignments(ViperButtonMappingFunction.Off);
        document.Devices[ProfileResolver.GetDeviceKey(Blade)] = new DeviceProfileSettings
        {
            Blade = new BladeProfileSettings { SnapTapEnabled = true },
            Viper = new ViperProfileSettings
            {
                ButtonAssignments = Assignments(ViperButtonMappingFunction.MouseButton),
            },
        };
        document.PluggedIn.Viper.ButtonAssignments = Assignments(ViperButtonMappingFunction.DoubleClick);

        var pluggedIn = ProfileResolver.Resolve(document, Blade, true);
        var onBattery = ProfileResolver.Resolve(document, Blade, false);

        Assert.True(pluggedIn.Blade.SnapTapEnabled);
        Assert.Equal(BladeProfileSettings.Product710DefaultMappingPreset, pluggedIn.Blade.MappingPreset);
        Assert.Equal(ViperButtonMappingFunction.DoubleClick, pluggedIn.Viper.ButtonAssignments![0].Function);
        Assert.Equal(ViperButtonMappingFunction.MouseButton, onBattery.Viper.ButtonAssignments![0].Function);
        Assert.NotSame(document.PluggedIn.Viper.ButtonAssignments, pluggedIn.Viper.ButtonAssignments);
    }

    [Fact]
    public async Task StoreRejectsExplicitEmptyShortcutCycle()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensynapse-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            var document = ProfileDocument.CreateDefault();
            document.Profiles[document.ActiveProfileName].Shortcuts.RefreshRateCycleHertz = [];

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProfileStore.ExportAsync(document, path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StoreRejectsUnknownMappingPreset()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensynapse-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            var document = ProfileDocument.CreateDefault();
            document.Global.Blade.MappingPreset = "unknown";

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProfileStore.ExportAsync(document, path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StoreRejectsUnsupportedPerformanceShortcutMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensynapse-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            var document = ProfileDocument.CreateDefault();
            document.Profiles[document.ActiveProfileName].Shortcuts.PerformanceCycleModes =
                [BladePerformanceMode.BatterySaver];

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProfileStore.ExportAsync(document, path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StoreRejectsWrongViperButtonSetEvenWhenCountIsSixteen()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensynapse-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            var document = ProfileDocument.CreateDefault();
            document.Global.Viper.ButtonAssignments = Assignments(ViperButtonMappingFunction.Off);
            document.Global.Viper.ButtonAssignments[0].ButtonId = 99;

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProfileStore.ExportAsync(document, path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static List<ViperButtonAssignmentProfile> Assignments(
        ViperButtonMappingFunction function) =>
        new byte[] { 1, 2, 3, 4, 5, 9, 10, 96 }
            .SelectMany(buttonId => new[]
            {
                Assignment(buttonId, ViperButtonMappingLayer.Normal, function),
                Assignment(buttonId, ViperButtonMappingLayer.HyperShift, function),
            })
            .ToList();

    private static ViperButtonAssignmentProfile Assignment(
        byte buttonId,
        ViperButtonMappingLayer layer,
        ViperButtonMappingFunction function) => new()
        {
            ProfileId = 1,
            ButtonId = buttonId,
            Layer = layer,
            Function = function,
            FunctionData = function switch
            {
                ViperButtonMappingFunction.MouseButton => [1],
                ViperButtonMappingFunction.DoubleClick => [1],
                _ => [],
            },
        };
}
