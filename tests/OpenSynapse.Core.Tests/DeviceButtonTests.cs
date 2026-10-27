using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DeviceButtonTests
{
    private static MacroInput DeviceKey(
        int deviceId = 0x00AA,
        int virtualKey = 0x7B,
        int scanCode = 0x58) => new()
    {
        Kind = MacroInputKind.DeviceButton,
        DeviceId = deviceId,
        DeviceName = "Razer Basilisk V3 Pro",
        VirtualKey = virtualKey,
        ScanCode = scanCode,
    };

    [Fact]
    public void DeviceButtonIsAValidTriggerButNotAPlaybackStep()
    {
        var input = DeviceKey();

        Assert.False(input.IsValid(allowPrimaryMouseButton: false, allowWheel: false));
        Assert.True(input.IsValid(
            allowPrimaryMouseButton: false,
            allowWheel: false,
            allowDeviceButton: true));

        var macro = new MacroDefinition
        {
            Trigger = new MacroTrigger { Input = input },
        };
        macro.ApplySafeDefaults();
        Assert.NotNull(macro.Trigger);
        Assert.Equal("Razer Basilisk V3 Pro", macro.Trigger!.Input.DeviceName);
    }

    [Fact]
    public void DeviceButtonMatchesItsDeviceAndKeyOnly()
    {
        var trigger = new MacroTrigger { Input = DeviceKey() };

        Assert.True(trigger.Matches(DeviceKey(), MacroModifierKeys.None));
        Assert.False(trigger.Matches(DeviceKey(deviceId: 0x0086), MacroModifierKeys.None));
        Assert.False(trigger.Matches(DeviceKey(virtualKey: 0x7C), MacroModifierKeys.None));
        Assert.False(trigger.Matches(
            new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = 0x7B,
                ScanCode = 0x58,
            },
            MacroModifierKeys.None));
    }

    [Fact]
    public void PrivateRazerKeyMatchesItsDeviceAndSlotOnly()
    {
        var input = new MacroInput
        {
            Kind = MacroInputKind.RazerKey,
            DeviceId = 0x00AA,
            DeviceName = "Razer Basilisk V3 Pro",
            RazerKeyCode = 81,
        };
        var trigger = new MacroTrigger { Input = input };

        Assert.True(input.IsValid(
            allowPrimaryMouseButton: false,
            allowWheel: false,
            allowDeviceButton: true));
        Assert.True(trigger.Matches(
            new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = 0x00AA,
                RazerKeyCode = 81,
            },
            MacroModifierKeys.None));
        Assert.False(trigger.Matches(
            new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = 0x00AA,
                RazerKeyCode = 82,
            },
            MacroModifierKeys.None));
        Assert.False(trigger.Matches(
            new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = 0x00CB,
                RazerKeyCode = 81,
            },
            MacroModifierKeys.None));
    }

    [Fact]
    public void PrivateKeyLabelsAreProductScoped()
    {
        Assert.Equal("DPI +", RazerPrivateKeyCatalog.Describe(0x0067, 32));
        Assert.Equal("DPI +", RazerPrivateKeyCatalog.Describe(0x00A8, 32));
        Assert.Equal("DPI +", RazerPrivateKeyCatalog.Describe(0x00E8, 32));
        Assert.Equal("M1", RazerPrivateKeyCatalog.Describe(0x0203, 32));
        Assert.Equal("DPI Clutch", RazerPrivateKeyCatalog.Describe(0x00AA, 81));
        Assert.Equal("Performance", RazerPrivateKeyCatalog.Describe(0x02C6, 211));
        Assert.Equal("Razer key 199", RazerPrivateKeyCatalog.Describe(0x00AA, 199));
    }

    [Fact]
    public async Task DeviceButtonRoundTripsThroughMacroFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-device-button-{Guid.NewGuid():N}.json");
        try
        {
            var macro = new MacroDefinition
            {
                Name = "Device button",
                Trigger = new MacroTrigger { Input = DeviceKey() },
                Steps =
                [
                    new MacroStep
                    {
                        Kind = MacroStepKind.Text,
                        Text = "ok",
                    },
                ],
            };

            await MacroFileStore.ExportAsync(macro, path);
            var loaded = await MacroFileStore.ImportAsync(path);

            Assert.Equal(MacroInputKind.DeviceButton, loaded.Trigger!.Input.Kind);
            Assert.Equal(0x00AA, loaded.Trigger.Input.DeviceId);
            Assert.Equal(0x7B, loaded.Trigger.Input.VirtualKey);
            Assert.Equal("Razer Basilisk V3 Pro", loaded.Trigger.Input.DeviceName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PrivateRazerKeyRoundTripsThroughMacroFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-razer-key-{Guid.NewGuid():N}.json");
        try
        {
            var macro = new MacroDefinition
            {
                Name = "Private key",
                Trigger = new MacroTrigger
                {
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.RazerKey,
                        DeviceId = 0x00AA,
                        DeviceName = "Razer Basilisk V3 Pro",
                        RazerKeyCode = 81,
                    },
                },
                Steps =
                [
                    new MacroStep
                    {
                        Kind = MacroStepKind.Text,
                        Text = "ok",
                    },
                ],
            };

            await MacroFileStore.ExportAsync(macro, path);
            var loaded = await MacroFileStore.ImportAsync(path);

            Assert.Equal(MacroInputKind.RazerKey, loaded.Trigger!.Input.Kind);
            Assert.Equal(0x00AA, loaded.Trigger.Input.DeviceId);
            Assert.Equal(81, loaded.Trigger.Input.RazerKeyCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
