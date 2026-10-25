using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerChromaStandardState(
    byte? BrightnessPercent,
    RazerChromaEffect? Effect,
    IReadOnlyList<byte> Colors,
    IReadOnlyList<string> Errors);

/// <summary>
/// Brightness and effect control over the class 0x10 Chroma channel shared by mice,
/// keyboards, lamps, ARGB controllers and docks that the OpenRazer catalogue does not cover.
/// </summary>
public sealed class RazerChromaStandardService
{
    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;

    internal RazerChromaStandardService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    public async Task<RazerChromaStandardState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        byte? brightness = null;
        try
        {
            brightness = RazerChromaStandardProtocol.ReadBrightnessPercent(
                await QueryAsync(RazerChromaStandardProtocol.GetBrightness, "read brightness", cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"brightness: {exception.Message}");
        }

        RazerChromaEffect? effect = null;
        IReadOnlyList<byte> colors = [];
        try
        {
            var current = RazerChromaStandardProtocol.ReadEffect(
                await QueryAsync(RazerChromaStandardProtocol.GetEffect, "read effect", cancellationToken)
                    .ConfigureAwait(false));
            effect = current.Effect;
            colors = current.Colors;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"effect: {exception.Message}");
        }

        return new RazerChromaStandardState(brightness, effect, colors, errors);
    }

    public async Task SetBrightnessAsync(byte percent, CancellationToken cancellationToken = default)
    {
        if (percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Brightness must be between 0 and 100.");
        }

        var previous = RazerChromaStandardProtocol.ReadBrightnessPercent(
            await QueryAsync(RazerChromaStandardProtocol.GetBrightness, "read brightness", cancellationToken)
                .ConfigureAwait(false));
        if (previous == percent)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerChromaStandardProtocol.SetBrightness(
                    transaction, RazerChromaStandardProtocol.ToBrightnessLevel(percent)),
                "set brightness",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerChromaStandardProtocol.ReadBrightnessPercent(
                await QueryAsync(RazerChromaStandardProtocol.GetBrightness, "read back brightness", cancellationToken)
                    .ConfigureAwait(false));
            if (Math.Abs(actual - percent) > 1)
            {
                throw new InvalidOperationException($"Brightness readback was {actual}%, expected {percent}%.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                transaction => RazerChromaStandardProtocol.SetBrightness(
                    transaction, RazerChromaStandardProtocol.ToBrightnessLevel(previous)),
                "restore brightness").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetEffectAsync(
        RazerChromaEffect effect,
        IReadOnlyList<byte> colors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        var payload = colors.ToArray();
        var previous = RazerChromaStandardProtocol.ReadEffect(
            await QueryAsync(RazerChromaStandardProtocol.GetEffect, "read effect", cancellationToken)
                .ConfigureAwait(false));
        if (previous.Effect == effect && previous.Colors.SequenceEqual(payload))
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerChromaStandardProtocol.SetEffect(transaction, effect, payload),
                "set effect",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerChromaStandardProtocol.ReadEffect(
                await QueryAsync(RazerChromaStandardProtocol.GetEffect, "read back effect", cancellationToken)
                    .ConfigureAwait(false));
            if (actual.Effect != effect)
            {
                throw new InvalidOperationException(
                    $"Effect readback was {actual.Effect}, expected {effect}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                transaction => RazerChromaStandardProtocol.SetEffect(transaction, previous.Effect, previous.Colors.ToArray()),
                "restore effect").ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryRestoreAsync(Func<byte, byte[]> request, string label)
    {
        try
        {
            await QueryAsync(request, label, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"chroma {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"chroma {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
        return await _transport.QueryPreparedAsync(
            _devicePath, report, _deviceWait, cancellationToken).ConfigureAwait(false);
    }

    private byte NextTransactionId()
    {
        if (_transactionId >= 0x1F)
        {
            _transactionId = 0;
        }
        else
        {
            _transactionId++;
        }

        return _transactionId;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;
}
