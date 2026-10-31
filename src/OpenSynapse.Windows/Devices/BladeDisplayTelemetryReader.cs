using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class BladeDisplayTelemetryReader
{
    private readonly RazerDeviceResolver _resolver;
    private readonly RazerCapabilityClient _client;
    private readonly BladeValidationState _validation;

    public BladeDisplayTelemetryReader(
        RazerDeviceResolver resolver,
        RazerCapabilityClient client,
        BladeValidationState validation)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public async ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default,
        bool verifyReadback = true)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade keyboard control channel is unavailable.");
        BladeTelemetryReader.EnsureControlWritesAdmitted(blade);
        RazerCapabilityClient.EnsureValidated(_validation.BrightnessPath, blade.Descriptor.Id, "Read the Blade keyboard brightness successfully first.");

        if (!verifyReadback)
        {
            await WriteBrightnessAsync(blade, brightness, cancellationToken);
            return brightness;
        }

        var original = await ReadBrightnessAsync(blade, cancellationToken);
        if (original == brightness)
        {
            return original;
        }

        try
        {
            await WriteBrightnessAsync(blade, brightness, cancellationToken);
            var actual = await ReadBrightnessAsync(blade, cancellationToken);
            if (actual != brightness)
            {
                throw new InvalidOperationException($"Brightness readback mismatch: wrote {brightness}, read {actual}.");
            }
            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBrightnessAsync(blade, original);
            var message = "Brightness update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the keyboard brightness immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<byte> ReadBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade keyboard control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validation.BrightnessPath, blade.Descriptor.Id, "Read the Blade keyboard brightness successfully first.");
        return await ReadBrightnessAsync(blade, cancellationToken);
    }

    public async ValueTask<BladeLighting2023Effect> SetBlade2023KeyboardEffectAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLighting2023Effect effect,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade keyboard control channel is unavailable.");
        if (!blade.Owns(CapabilityIds.KeyboardEffectSet))
        {
            throw new InvalidOperationException(
                "Firmware-rendered keyboard effects are not available on this Blade model.");
        }

        RazerCapabilityClient.EnsureValidated(
            _validation.KeyboardEffectPath,
            blade.Descriptor.Id,
            "Read the Blade keyboard effect successfully first.");

        var original = BladeLighting2023Protocol.ParseEffect(
            await _client.QueryAsync(blade, CapabilityIds.KeyboardEffectGet, cancellationToken));
        if (original == effect)
        {
            return original;
        }

        var request = effect switch
        {
            BladeLighting2023Effect.Off => BladeLighting2023Protocol.CreateOffRequest(),
            BladeLighting2023Effect.Breathing => BladeLighting2023Protocol.CreateBreathingRequest(),
            BladeLighting2023Effect.Spectrum => BladeLighting2023Protocol.CreateSpectrumRequest(),
            BladeLighting2023Effect.Wave => BladeLighting2023Protocol.CreateWaveRequest(),
            _ => throw new ArgumentOutOfRangeException(nameof(effect)),
        };
        try
        {
            await _client.QueryBuiltRequestAsync(blade, CapabilityIds.KeyboardEffectSet, request, cancellationToken);
        }
        catch (Exception exception) when (BladeLightingRuntimeDialect.IsExplicitRejection(exception))
        {
            BladeLightingRuntimeDialect.Downgrade(blade.Descriptor.ProductId, exception.Message);
            throw;
        }

        var actual = BladeLighting2023Protocol.ParseEffect(
            await _client.QueryAsync(blade, CapabilityIds.KeyboardEffectGet, cancellationToken));
        if (actual != effect)
        {
            throw new InvalidOperationException(
                $"Keyboard effect readback mismatch: wrote {effect}, read {actual}.");
        }

        return actual;
    }

    internal async Task<byte> ReadBrightnessAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(device, CapabilityIds.KeyboardBrightnessGet, cancellationToken);
        if (device.Descriptor.ProtocolFamily is "blade-597" or "blade-710-chroma")
        {
            // Chroma brightness answers profileId, regionId, raw 0..255. The keyboard
            // backlight is the BacklightLED region (5) on these families.
            RazerCapabilityClient.EnsureDataSize(response, 3, "keyboard brightness");
            var arguments = response.AsSpan(RazerFeatureReport.ArgumentsOffset);
            if (arguments[0] != 0x01 || arguments[1] != 0x05)
            {
                throw new InvalidOperationException("Blade returned an incorrect Chroma brightness object.");
            }

            return checked((byte)Math.Ceiling(arguments[2] * 100d / 255d));
        }
        // The 671 family answers the selector-shaped brightness report (store, region,
        // raw 0..255). Reading arguments[1] returns the constant keyboard region (5)
        // instead of the level, so this family always uses the selector decoder.
        if (device.Descriptor.ProtocolFamily == "blade-671")
        {
            return BladeLighting2023Protocol.ParseBrightness(response);
        }

        RazerCapabilityClient.EnsureDataSize(response, 2, "keyboard brightness");
        return response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    private Task WriteBrightnessAsync(
        ResolvedRazerDevice device,
        byte brightness,
        CancellationToken cancellationToken)
    {
        if (device.Descriptor.ProtocolFamily is "blade-597" or "blade-710-chroma")
        {
            // These families use the Chroma brightness object: profile 1, BacklightLED
            // region 5, and an 8-bit raw value. Floor keeps a 50% write reading back as
            // 50% after the read path's ceil conversion.
            var raw = (byte)Math.Clamp(brightness * 255 / 100, 0, 255);
            return _client.QueryAsync(
                device,
                CapabilityIds.KeyboardBrightnessSet,
                new byte[] { 0x01, 0x05, raw },
                cancellationToken);
        }

        return device.Descriptor.ProtocolFamily == "blade-671"
            ? _client.QueryBuiltRequestAsync(
                device,
                CapabilityIds.KeyboardBrightnessSet,
                BladeLighting2023Protocol.CreateSetBrightnessRequest(brightness),
                cancellationToken)
            : _client.QueryAsync(
                device, CapabilityIds.KeyboardBrightnessSet, new byte[] { 0x01, brightness }, cancellationToken);
    }

    private async Task<bool> TryRestoreBrightnessAsync(ResolvedRazerDevice device, byte original)
    {
        try
        {
            await WriteBrightnessAsync(device, original, CancellationToken.None);
            return await ReadBrightnessAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    public async ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLogoMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (BladeLogoMode.Off or BladeLogoMode.Static or BladeLogoMode.Breathing))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode), "Unsupported Logo mode.");
        }

        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade Logo control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validation.LogoPath, blade.Descriptor.Id, "Read the Blade Logo state successfully first.");

        var original = await ReadLogoStateAsync(blade, cancellationToken);
        if (original.CombinedMode == mode)
        {
            return mode;
        }

        try
        {
            await WriteLogoStateAsync(blade, mode, cancellationToken);
            var actual = await ReadLogoStateAsync(blade, cancellationToken);
            if (actual.CombinedMode != mode ||
                (mode == BladeLogoMode.Static && actual.PoweredMode != BladeLogoMode.Static))
            {
                throw new InvalidOperationException(
                    $"Logo readback mismatch: wrote {mode}, read {actual.CombinedMode}/{actual.PoweredMode}.");
            }

            return actual.CombinedMode;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreLogoStateAsync(blade, original);
            var message = "Logo update failed: " + exception.Message + " " +
                (restored ? "The original state was restored." : "Original state restoration failed; check the Logo immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    internal async Task<BladeLogoState> ReadLogoStateAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var powerResponse = await _client.QueryAsync(device, CapabilityIds.LogoPowerGet, cancellationToken);
        var modeResponse = await _client.QueryAsync(device, CapabilityIds.LogoModeGet, cancellationToken);
        var power = BladeLogoProtocol.ParsePower(
            powerResponse, RazerCapabilityClient.CreateRequest(device, CapabilityIds.LogoPowerGet));
        var poweredMode = BladeLogoProtocol.ParseMode(
            modeResponse, RazerCapabilityClient.CreateRequest(device, CapabilityIds.LogoModeGet));
        return new BladeLogoState(power, poweredMode);
    }

    private async Task WriteLogoStateAsync(
        ResolvedRazerDevice device,
        BladeLogoMode combinedMode,
        CancellationToken cancellationToken)
    {
        if (combinedMode == BladeLogoMode.Breathing)
        {
            await _client.QueryBuiltRequestAsync(
                device,
                CapabilityIds.LogoEffectSet,
                BladeSynapsePolicyProtocol.CreateSetLogoEffectRequest(BladeLogoMode.Breathing),
                cancellationToken);
            await _client.QueryBuiltRequestAsync(
                device,
                CapabilityIds.LogoStateSet,
                BladeSynapsePolicyProtocol.CreateSetLogoStateRequest(BladeLogoMode.Breathing),
                cancellationToken);
            return;
        }

        if (combinedMode == BladeLogoMode.Static)
        {
            await _client.QueryBuiltRequestAsync(
                device, CapabilityIds.LogoModeSet, BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Static), cancellationToken);
        }
        else if (combinedMode != BladeLogoMode.Off)
        {
            throw new ArgumentOutOfRangeException(nameof(combinedMode));
        }

        var powerRequest = BladeLogoProtocol.CreateSetPowerRequest(combinedMode != BladeLogoMode.Off);
        await _client.QueryBuiltRequestAsync(device, CapabilityIds.LogoPowerSet, powerRequest, cancellationToken);
    }

    private async Task<bool> TryRestoreLogoStateAsync(
        ResolvedRazerDevice device,
        BladeLogoState original)
    {
        try
        {
            await _client.QueryBuiltRequestAsync(
                device,
                CapabilityIds.LogoModeSet,
                BladeLogoProtocol.CreateSetModeRequest(original.PoweredMode),
                CancellationToken.None);
            await _client.QueryBuiltRequestAsync(
                device,
                CapabilityIds.LogoPowerSet,
                BladeLogoProtocol.CreateSetPowerRequest(original.Powered),
                CancellationToken.None);
            return await ReadLogoStateAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    internal async Task<BladeStartupAnimationState> ReadStartupAnimationAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var request = RazerCapabilityClient.CreateRequest(device, CapabilityIds.StartupAnimationGet);
        var response = await _client.QueryAsync(device, CapabilityIds.StartupAnimationGet, cancellationToken);
        return BladeSynapsePolicyProtocol.ParseStartupAnimation(response, request);
    }

    public async ValueTask<bool> SetBladeStartupAnimationAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade startup-animation control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(
            _validation.StartupAnimationPath,
            blade.Descriptor.Id,
            "Read the Blade startup-animation state successfully first.");

        var original = await ReadStartupAnimationAsync(blade, cancellationToken);
        if (original.Enabled == enabled)
        {
            return original.Enabled;
        }

        try
        {
            await WriteStartupAnimationAsync(blade, enabled, cancellationToken);
            var actual = await ReadStartupAnimationAsync(blade, cancellationToken);
            if (actual.Enabled != enabled)
            {
                throw new InvalidOperationException(
                    $"Startup-animation readback mismatch: wrote {(enabled ? "enabled" : "disabled")}, " +
                    $"read {(actual.Enabled ? "enabled" : "disabled")}.");
            }

            return actual.Enabled;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreStartupAnimationAsync(blade, original.Enabled);

            var message = "Startup-animation update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the startup animation immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task WriteStartupAnimationAsync(
        ResolvedRazerDevice device,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var request = BladeSynapsePolicyProtocol.CreateSetStartupAnimationRequest(enabled);
        _ = await _client.QueryBuiltRequestAsync(
            device, CapabilityIds.StartupAnimationSet, request, cancellationToken);
    }

    private async Task<bool> TryRestoreStartupAnimationAsync(
        ResolvedRazerDevice device,
        bool original)
    {
        try
        {
            await WriteStartupAnimationAsync(device, original, CancellationToken.None);
            return (await ReadStartupAnimationAsync(device, CancellationToken.None)).Enabled == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    internal async Task<BladeNativeDisplayMode> ReadNativeDisplayModeAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var request = RazerCapabilityClient.CreateRequest(device, CapabilityIds.NativeDisplayModeGet);
        var response = await _client.QueryAsync(
            device, CapabilityIds.NativeDisplayModeGet, cancellationToken);
        return BladeProduct710Protocol.ParseNativeDisplayMode(response, request);
    }

    public async ValueTask<BladeNativeDisplayMode> SetBladeNativeDisplayModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade native-display-mode control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(
            _validation.NativeDisplayModePath,
            blade.Descriptor.Id,
            "Read the Blade native display mode successfully first.");

        var original = await ReadNativeDisplayModeAsync(blade, cancellationToken);
        if (original == mode)
        {
            return original;
        }

        try
        {
            await WriteNativeDisplayModeAsync(blade, mode, cancellationToken);
            var actual = await ReadNativeDisplayModeAsync(blade, cancellationToken);
            if (actual != mode)
            {
                throw new InvalidOperationException(
                    $"Native display mode readback mismatch: wrote {mode}, read {actual}.");
            }

            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreNativeDisplayModeAsync(blade, original);
            var message = "Native display mode update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the display mode immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    private Task WriteNativeDisplayModeAsync(
        ResolvedRazerDevice device,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken) =>
        _client.QueryBuiltRequestAsync(
            device,
            CapabilityIds.NativeDisplayModeSet,
            BladeProduct710Protocol.CreateSetNativeDisplayModeRequest(mode),
            cancellationToken);

    private async Task<bool> TryRestoreNativeDisplayModeAsync(
        ResolvedRazerDevice device,
        BladeNativeDisplayMode original)
    {
        try
        {
            await WriteNativeDisplayModeAsync(device, original, CancellationToken.None);
            return await ReadNativeDisplayModeAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    public ValueTask<BladeDisplayMuxStatus> ReadBladeDisplayMuxStatusAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        BladeDisplayMuxReader.ReadAsync(_resolver, _client, devices, cancellationToken);

    public ValueTask<BladeDisplayMuxSwitchResult> SetBladeDisplayMuxModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken = default) =>
        BladeDisplayMuxReader.SetAsync(_resolver, _client, devices, mode, cancellationToken);
}

internal sealed record BladeLogoState(
    bool Powered,
    BladeLogoMode PoweredMode)
{
    internal BladeLogoMode CombinedMode => BladeLogoProtocol.Combine(Powered, PoweredMode);
}
