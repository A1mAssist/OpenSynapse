using System.Buffers.Binary;
using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal readonly record struct RazerLegacyKeypadIdentity(ulong Value, ushort ProductId)
{
    // The official driver uses %016x, which formats the low DWORD even on x64.
    internal string Path => @"\\.\Razer_" + unchecked((uint)Value).ToString("x16");
}

internal sealed class RazerLegacyKeypadControl(RazerLegacyKeypadIdentity identity,
    Func<string, byte[], int, uint, byte[]>? transfer = null,
    Func<ushort, bool>? isHidPresent = null)
{
    private readonly Func<string, byte[], int, uint, byte[]> _transfer = transfer ?? RazerFilterControlTransport.Transfer;
    private readonly Func<ushort, bool> _isHidPresent = isHidPresent ?? RazerFilterEndpointDiscovery.IsHidProductPresent;
    internal RazerLegacyKeypadIdentity Identity { get; } = identity;

    internal static RazerLegacyKeypadControl Discover(string featurePath, ushort productId,
        CancellationToken cancellationToken)
    {
        if (!RazerKeypadInputCatalog.IsLegacy(productId)) throw new ArgumentOutOfRangeException(nameof(productId));
        if (!RazerFilterEndpointDiscovery.IsOnlyHidContainer(featurePath, productId, cancellationToken))
            throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.AmbiguousDevice,
                "Legacy keypad mapping requires exactly one connected device of this model.");
        var selected = Select(Enumerate(), productId)
            ?? throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.DriverMissing,
                "Keypad mapping requires this model's Razer rzudd filter driver.");
        return new(selected);
    }

    internal static RazerLegacyKeypadIdentity? Select(IReadOnlyList<RazerLegacyKeypadIdentity> devices, ushort productId)
    {
        var matches = devices.Where(device => device.ProductId == productId).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || devices.Count(device => (uint)device.Value == (uint)matches[0].Value) != 1)
            throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.AmbiguousDevice,
                "The legacy keypad driver identity is ambiguous.");
        return matches[0];
    }

    internal static IReadOnlyList<RazerLegacyKeypadIdentity> Enumerate(
        Func<string, byte[], int, uint, byte[]>? transfer = null)
    {
        transfer ??= RazerFilterControlTransport.Transfer;
        var devices = new List<RazerLegacyKeypadIdentity>();
        for (uint index = 0; index < 128; index++)
        {
            byte[] reply;
            try
            {
                reply = transfer(@"\\.\RzUdd", RazerLegacyKeypadInputProtocol.Enumerate(index), 32,
                    RazerLegacyKeypadInputProtocol.EnumerateIoctl);
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 31) { return devices; }
            catch (Win32Exception error) when (index == 0 && error.NativeErrorCode is 2 or 3) { return devices; }
            if (RazerLegacyKeypadInputProtocol.Status(reply) == unchecked((int)0xC0000001)) return devices;
            CheckStatus(reply);
            if (reply.Length < 18) throw new InvalidDataException("The legacy device identity reply is truncated.");
            var identity = new RazerLegacyKeypadIdentity(BinaryPrimitives.ReadUInt64LittleEndian(reply.AsSpan(8)),
                BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(16)));
            if (identity.Value == 0) throw new InvalidDataException("The legacy device identity is empty.");
            devices.Add(identity);
        }
        throw new InvalidDataException("The legacy device enumeration did not terminate.");
    }

    internal void RequireAvailable()
    {
        if (!IsPresent(Enumerate(_transfer), Identity)) throw new IOException("The legacy keypad was disconnected or replaced.");
        if (QueryCapture(false) || (Identity.ProductId == 0x0111 && QueryCapture(true)))
            throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.DriverInUse,
                "Another application is already capturing this keypad. Exit Razer Synapse before enabling mapping.");
    }

    internal static bool IsPresent(IReadOnlyList<RazerLegacyKeypadIdentity> devices, RazerLegacyKeypadIdentity identity)
    {
        if (!devices.Contains(identity)) return false;
        if (devices.Count(device => (uint)device.Value == (uint)identity.Value) != 1)
            throw new IOException("The legacy keypad endpoint identity is ambiguous.");
        return true;
    }

    internal void Enable()
    {
        if (!IsPresent(Enumerate(_transfer), Identity)) throw new IOException("The legacy keypad was disconnected or replaced.");
        SetCapture(false, true);
        if (Identity.ProductId == 0x0111) SetCapture(true, true);
    }

    internal void Restore()
    {
        var devices = Enumerate(_transfer);
        if (!IsPresent(devices, Identity))
        {
            // ERROR_GEN_FAILURE also ends enumeration after internal driver errors.
            if (!devices.Any(device => device.ProductId == Identity.ProductId) && _isHidPresent(Identity.ProductId))
                throw new IOException("The connected keypad is missing from driver enumeration; recovery remains pending.");
            return;
        }
        var errors = new List<Exception>();
        try { SetCapture(false, false); } catch (Exception error) { errors.Add(error); }
        if (Identity.ProductId == 0x0111)
        {
            try { SetCapture(true, false); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Legacy keypad capture could not be restored.", errors);
    }

    private bool QueryCapture(bool mouse)
    {
        var reply = Command(mouse ? 28u : 21u);
        if (reply.Length < 12) throw new InvalidDataException("The legacy keypad capture reply is truncated.");
        return BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(8)) != 0;
    }

    private void SetCapture(bool mouse, bool enabled)
    {
        Command(mouse ? enabled ? 29u : 30u : enabled ? 22u : 23u);
        if (QueryCapture(mouse) != enabled) throw new IOException("The legacy keypad capture state did not change.");
    }

    private byte[] Command(uint command)
    {
        var reply = _transfer(Identity.Path,
            RazerLegacyKeypadInputProtocol.CaptureCommand(command), 16, RazerLegacyKeypadInputProtocol.ControlIoctl);
        CheckStatus(reply);
        return reply;
    }

    private static void CheckStatus(byte[] reply)
    {
        var status = RazerLegacyKeypadInputProtocol.Status(reply);
        if (status < 0) throw new IOException($"Legacy keypad driver returned NTSTATUS 0x{status:X8}.");
    }
}
