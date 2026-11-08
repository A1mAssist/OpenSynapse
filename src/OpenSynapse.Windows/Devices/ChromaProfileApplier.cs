using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies the shared Chroma channel plus the optional 320-byte ARGB frames.
/// Brightness and effect reads come from the device; ARGB frames have no readback
/// command and are therefore reported as applied but unverified.
/// </summary>
public sealed class ChromaProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.Lighting];
    private readonly Func<DeviceDescriptor, CancellationToken, Task<RazerChromaStandardService?>> _connect;
    private readonly RazerHidSpecialLightingService _specialLighting;
    private readonly Action<string>? _diagnostic;

    public ChromaProfileApplier(Action<string>? diagnostic = null)
        : this(ConnectAsync, new RazerHidSpecialLightingService(diagnostic), diagnostic)
    {
    }

    public ChromaProfileApplier(
        Func<DeviceDescriptor, CancellationToken, Task<RazerChromaStandardService?>> connect,
        RazerHidSpecialLightingService? specialLighting = null,
        Action<string>? diagnostic = null)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _specialLighting = specialLighting ?? new RazerHidSpecialLightingService(diagnostic);
        _diagnostic = diagnostic;
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category != DeviceCategory.Lighting)
        {
            return new ProfileDeviceApplyResult(context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var service = await _connect(context.Device, cancellationToken);
        if (service is null)
        {
            return Failed(context.Device.Id, "The Chroma device is not available.");
        }

        RazerChromaStandardState? state = null;
        try
        {
            state = await service.ReadStateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"chroma profile read: {exception.Message}");
        }

        var key = string.IsNullOrWhiteSpace(state?.SerialNumber)
            ? ProfileResolver.GetDeviceKey(context.Device)
            : state.SerialNumber;
        var settings = ProfileResolver.ResolveWithDeviceKey(
            context.Document,
            context.Device,
            context.PowerSource,
            key).Chroma;

        var results = new List<ProfileSettingApplyResult>();
        await ApplyBrightnessAsync(service, settings, state, results, cancellationToken);
        await ApplyEffectAsync(service, settings, state, results, cancellationToken);
        await ApplyArgbFramesAsync(context, settings, results, cancellationToken);
        return new ProfileDeviceApplyResult(context.Device.Id, Status(results), results);
    }

    private static async Task ApplyBrightnessAsync(
        RazerChromaStandardService service,
        ChromaProfileSettings settings,
        RazerChromaStandardState? state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.BrightnessPercent is not { } desired)
        {
            return;
        }

        const string key = "chroma.brightness";
        if (state?.BrightnessPercent == desired)
        {
            results.Add(new(key, ProfileSettingApplyStatus.Skipped));
            return;
        }

        try
        {
            await service.SetBrightnessAsync((byte)desired, cancellationToken);
            results.Add(new(key, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static async Task ApplyEffectAsync(
        RazerChromaStandardService service,
        ChromaProfileSettings settings,
        RazerChromaStandardState? state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Effect))
        {
            return;
        }

        const string key = "chroma.effect";
        if (!Enum.TryParse<RazerChromaEffect>(settings.Effect, ignoreCase: true, out var effect) ||
            !Enum.IsDefined(effect))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, "The Chroma effect is not recognized."));
            return;
        }

        var colors = BuildColors(effect, settings, out var error);
        if (error is not null)
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, error));
            return;
        }

        if (state?.Effect == effect && ColorsEqual(state.Colors, colors))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Skipped));
            return;
        }

        try
        {
            await service.SetEffectAsync(effect, colors, cancellationToken);
            results.Add(new(key, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private async Task ApplyArgbFramesAsync(
        DeviceProfileApplyContext context,
        ChromaProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.ArgbFrames is not { Count: > 0 } frames)
        {
            return;
        }

        RazerHidSpecialLightingConnection? connection;
        try
        {
            var connections = await _specialLighting.DiscoverAsync(cancellationToken);
            connection = connections.FirstOrDefault(item =>
                item.Kind == RazerHidSpecialLightingKind.Argb320 &&
                item.IsReady &&
                (item.ProductId == context.Device.ProductId || item.ProductId == 0x0F1F));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new("chroma.argb", ProfileSettingApplyStatus.Failed, exception.Message));
            return;
        }

        if (connection is null)
        {
            results.Add(new(
                "chroma.argb",
                ProfileSettingApplyStatus.Failed,
                "The ARGB controller is not available."));
            return;
        }

        try
        {
            await _specialLighting.SetArgbCustomFrameModeAsync(connection, cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"chroma custom frame mode: {exception.Message}");
        }

        foreach (var (portText, encoded) in frames)
        {
            var key = $"chroma.argb.{portText}";
            if (!byte.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
                port > RazerHidArgbFrame.MaxChannel)
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, "The ARGB port is invalid."));
                continue;
            }

            if (!TryDecodeFrame(encoded, out var colors, out var error))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, error));
                continue;
            }

            try
            {
                await _specialLighting.SetArgbFrameAsync(connection, port, colors, cancellationToken);
                results.Add(new(key, ProfileSettingApplyStatus.AppliedUnverified));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }
    }

    private static bool TryDecodeFrame(
        string encoded,
        out IReadOnlyList<RazerHidColor> colors,
        out string error)
    {
        colors = [];
        error = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length == 0 || bytes.Length % 3 != 0 || bytes.Length / 3 > RazerHidArgbFrame.MaxLeds)
            {
                error = "The ARGB frame must contain 1..80 RGB triples.";
                return false;
            }

            var parsed = new RazerHidColor[bytes.Length / 3];
            for (var index = 0; index < parsed.Length; index++)
            {
                parsed[index] = new RazerHidColor(
                    bytes[index * 3],
                    bytes[(index * 3) + 1],
                    bytes[(index * 3) + 2]);
            }

            colors = parsed;
            return true;
        }
        catch (FormatException)
        {
            error = "The ARGB frame is not valid base64.";
            return false;
        }
    }

    private static byte[] BuildColors(
        RazerChromaEffect effect,
        ChromaProfileSettings settings,
        out string? error)
    {
        error = null;
        if (effect is not (RazerChromaEffect.Static or RazerChromaEffect.Breathing))
        {
            return [];
        }

        if (!TryParseColor(settings.PrimaryColor, out var primary))
        {
            error = "The primary Chroma color is invalid.";
            return [];
        }

        if (effect == RazerChromaEffect.Static)
        {
            return primary;
        }

        if (!TryParseColor(settings.SecondaryColor, out var secondary))
        {
            error = "The secondary Chroma color is invalid.";
            return [];
        }

        return [.. primary, .. secondary];
    }

    private static bool TryParseColor(string? text, out byte[] rgb)
    {
        rgb = [];
        if (text is null)
        {
            return false;
        }

        text = text.Trim().TrimStart('#');
        if (text.Length != 6)
        {
            return false;
        }

        var parsed = new byte[3];
        for (var index = 0; index < parsed.Length; index++)
        {
            if (!byte.TryParse(
                    text.AsSpan(index * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out parsed[index]))
            {
                return false;
            }
        }

        rgb = parsed;
        return true;
    }

    private static bool ColorsEqual(IReadOnlyList<byte> first, IReadOnlyList<byte> second) =>
        first.Count == second.Count && first.SequenceEqual(second);

    private static async Task<RazerChromaStandardService?> ConnectAsync(
        DeviceDescriptor device,
        CancellationToken cancellationToken)
    {
        var connections = await RazerChromaStandardService.FindAllAsync(cancellationToken: cancellationToken);
        var connection = connections.FirstOrDefault(item =>
            item.ProductId == device.ProductId &&
            (string.Equals(item.DevicePath, device.Id, StringComparison.OrdinalIgnoreCase) ||
             !connections.Any(other => other.ProductId == device.ProductId)));
        connection ??= connections.FirstOrDefault(item => item.ProductId == device.ProductId);
        return connection is null
            ? null
            : new RazerChromaStandardService(
                new RazerFeatureTransport(),
                connection.DevicePath,
                TimeSpan.FromMilliseconds(20));
    }

    private static ProfileDeviceApplyStatus Status(List<ProfileSettingApplyResult> results) =>
        results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;

    private static ProfileDeviceApplyResult Failed(string deviceId, string error) =>
        new(deviceId, ProfileDeviceApplyStatus.Failed,
            [new("device", ProfileSettingApplyStatus.Failed, error)]);

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException or
            System.ComponentModel.Win32Exception;
}
