using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// AMD keeps the package wall in the SMU mailbox. PawnIO's RyzenSMU module owns the
/// per-family mailbox registers, so this class only speaks its IOCTLs. The power
/// commands below are the MP1 messages AMD uses for STAPM, fast PPT and slow PPT.
/// </summary>
public sealed class RyzenSmuCpuPowerLimit : ICpuPowerLimitController
{
    private const double MilliwattsPerWatt = 1000;

    // Code names returned by PawnIO.Modules 0.2.11 RyzenSMU.p. Only mobile APUs whose
    // MP1 power messages are the 0x14/0x15/0x16 set are listed.
    private const ulong Renoir = 1;
    private const ulong Rembrandt = 10;
    private const ulong Cezanne = 13;
    private const ulong Lucienne = 22;
    private const ulong Phoenix = 23;
    private const ulong Phoenix2 = 24;
    private const ulong Mendocino = 25;
    private const ulong HawkPoint = 30;
    private const ulong StrixPoint = 31;
    private const ulong StrixHalo = 32;
    private const ulong KrackanPoint = 33;
    private const ulong KrackanPoint2 = 34;

    private readonly TryReadTable _readTable;
    private readonly TrySendCommand _send;
    private readonly Action _dispose;
    private readonly SmuCommands _commands;
    private readonly CpuPowerLimit _firmware;
    private readonly PowerLimits? _firmwareLimits;
    private bool _disposed;

    private RyzenSmuCpuPowerLimit(
        RyzenSmuSession session,
        SmuCommands commands,
        CpuPowerLimit firmware,
        ulong[] firmwareTable)
        : this(commands, firmware, session.TryReadPowerTable, session.TrySend, session.Dispose, firmwareTable)
    {
    }

    internal RyzenSmuCpuPowerLimit(
        SmuCommands commands,
        CpuPowerLimit firmware,
        TryReadTable readTable,
        TrySendCommand send,
        Action dispose,
        ulong[] firmwareTable)
    {
        _readTable = readTable;
        _send = send;
        _dispose = dispose;
        _commands = commands;
        _firmware = firmware;
        _firmwareLimits = TryDecodeLimits(firmwareTable, out var limits, out _) ? limits : null;
    }

    /// <summary>
    /// Loads RyzenSMU.bin, resolves the CPU family and snapshots the power table.
    /// Only access-denied is worth retrying through the machine service.
    /// </summary>
    public static CpuPowerLimitAvailability TryOpen(
        out ICpuPowerLimitController? controller,
        out string? detail)
    {
        controller = null;
        var availability = RyzenSmuSession.TryOpen(out var session, out detail);
        if (session is null)
        {
            return availability;
        }

        try
        {
            var commands = CommandsFor(session.CodeName);
            if (commands is null)
            {
                session.Dispose();
                detail = "This Ryzen family does not use the supported SMU power messages.";
                return CpuPowerLimitAvailability.Failed;
            }

            if (!session.TryReadPowerTable(out var table, out var readError) ||
                !TryDecode(table, out var firmware, out readError))
            {
                session.Dispose();
                detail = readError;
                return CpuPowerLimitAvailability.Failed;
            }

            controller = new RyzenSmuCpuPowerLimit(session, commands.Value, firmware, table);
            detail = null;
            return CpuPowerLimitAvailability.Available;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public CpuPowerLimitReport Read()
    {
        ThrowIfDisposed();
        return _readTable(out var table, out var error) &&
            TryDecode(table, out var limit, out error)
                ? BuildReport(limit)
                : CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error);
    }

    public CpuPowerLimitReport Apply(CpuPowerLimit limit)
    {
        ThrowIfDisposed();
        return Write(limit.Clamp(CpuPowerLimit.MaximumWatts));
    }

    public CpuPowerLimitReport RestoreTo(double pl1Watts, double pl2Watts)
    {
        ThrowIfDisposed();
        var request = new CpuPowerLimit(pl1Watts, pl2Watts, 0, 0)
            .Clamp(CpuPowerLimit.MaximumRestoreWatts);
        return Write(request);
    }

    public CpuPowerLimitReport RestoreFirmware()
    {
        ThrowIfDisposed();
        if (_firmwareLimits is not { } limits ||
            limits.Stapm > ToMilliwatts(CpuPowerLimit.MaximumRestoreWatts) ||
            limits.Slow > ToMilliwatts(CpuPowerLimit.MaximumRestoreWatts) ||
            limits.Fast > ToMilliwatts(CpuPowerLimit.MaximumRestoreWatts))
        {
            return Failed("The opening SMU snapshot is incomplete or outside the restore ceiling; nothing was written.");
        }

        return Write(limits);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }

    internal static SmuCommands? CommandsFor(ulong codeName) => codeName switch
    {
        Renoir or Lucienne or Cezanne or Rembrandt or Phoenix or Phoenix2 or
            Mendocino or HawkPoint or StrixPoint or StrixHalo or
            KrackanPoint or KrackanPoint2 => new SmuCommands(0x14, 0x15, 0x16),
        _ => null,
    };

    internal static bool TryDecode(
        ulong[] table,
        out CpuPowerLimit limit,
        out string? error)
    {
        limit = default;
        if (table.Length < 3)
        {
            error = "The SMU power table is shorter than the two PPT limits.";
            return false;
        }

        // This table stores watts. The mailbox write below still takes milliwatts.
        var fast = FloatAt(table, 2);
        var slow = FloatAt(table, 4);
        if (!(fast >= 1 && slow >= 1 && fast < 500 && slow < 500))
        {
            error = $"The SMU power table returned implausible limits ({slow} W / {fast} W).";
            return false;
        }

        limit = new CpuPowerLimit(
            Math.Min(slow, fast),
            Math.Max(slow, fast),
            CpuPowerLimit.MinimumTimeSeconds,
            CpuPowerLimit.MaximumTimeSeconds);
        error = null;
        return true;
    }

    private static float FloatAt(ulong[] table, int index) =>
        BitConverter.Int32BitsToSingle(
            (int)((table[index / 2] >> ((index % 2) * 32)) & 0xFFFFFFFF));

    internal static uint ToMilliwatts(double watts) =>
        (uint)Math.Round(watts * MilliwattsPerWatt);

    private CpuPowerLimitReport Write(CpuPowerLimit limit) => Write(new PowerLimits(
        ToMilliwatts(limit.Pl1Watts), ToMilliwatts(limit.Pl1Watts), ToMilliwatts(limit.Pl2Watts)));

    private CpuPowerLimitReport Write(PowerLimits requested)
    {
        // A failed mailbox call may already have changed a limit. Snapshot all three
        // independent targets before the first write, including STAPM (PM float 0).
        if (!TryReadLimits(out var previous, out var error))
        {
            return Failed($"Power limits could not be snapshotted; nothing was written. {error}");
        }

        if (TryWrite(_commands.Stapm, requested.Stapm, out error) &&
            TryWrite(_commands.Slow, requested.Slow, out error) &&
            TryWrite(_commands.Fast, requested.Fast, out error))
        {
            if (TryReadLimits(out var actual, out error))
            {
                if (actual == requested)
                {
                    return BuildReport(actual.ToLimit());
                }

                error = $"SMU readback mismatch: requested {requested}, read {actual}.";
            }
        }

        var errors = new List<string> { error ?? "The SMU power write failed." };
        // Never short-circuit recovery: one rejected restore must not leave the
        // remaining targets at their requested values.
        Restore(_commands.Stapm, previous.Stapm);
        Restore(_commands.Slow, previous.Slow);
        Restore(_commands.Fast, previous.Fast);
        if (!TryReadLimits(out var restored, out error))
        {
            errors.Add($"Rollback could not be verified: {error}");
            return Failed(string.Join(" ", errors));
        }

        errors.Add(restored == previous
            ? "Previous STAPM, Slow and Fast limits restored and verified."
            : $"Rollback failed: expected {previous}, read {restored}.");
        return BuildReport(restored.ToLimit()) with
        {
            Availability = CpuPowerLimitAvailability.Failed,
            Detail = string.Join(" ", errors),
        };

        void Restore(uint command, uint value)
        {
            if (!TryWrite(command, value, out var restoreError))
            {
                errors.Add($"Rollback command 0x{command:X2} failed: {restoreError}");
            }
        }
    }

    private bool TryReadLimits(out PowerLimits limits, out string? error)
    {
        limits = default;
        try
        {
            return _readTable(out var table, out error) && TryDecodeLimits(table, out limits, out error);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryDecodeLimits(ulong[] table, out PowerLimits limits, out string? error)
    {
        limits = default;
        if (!TryDecode(table, out _, out error)) return false;

        // RyzenAdj lib/api.c: STAPM/Fast/Slow byte offsets 0x0/0x8/0x10.
        var stapm = FloatAt(table, 0);
        if (!(stapm >= 1 && stapm < 500))
        {
            error = $"The SMU table returned an implausible STAPM limit ({stapm} W).";
            return false;
        }

        limits = new PowerLimits(ToMilliwatts(stapm),
            ToMilliwatts(FloatAt(table, 4)), ToMilliwatts(FloatAt(table, 2)));
        return true;
    }

    private bool TryWrite(uint command, uint value, out string? error)
    {
        try
        {
            return _send(command, value, out error);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsIoFailure(Exception exception) =>
        exception is InvalidOperationException or IOException or UnauthorizedAccessException;

    private static CpuPowerLimitReport Failed(string? error) =>
        CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error) with { UsesDirectSmu = true };

    private readonly record struct PowerLimits(uint Stapm, uint Slow, uint Fast)
    {
        internal CpuPowerLimit ToLimit() => new(
            Math.Min(Slow, Fast) / MilliwattsPerWatt,
            Math.Max(Slow, Fast) / MilliwattsPerWatt,
            CpuPowerLimit.MinimumTimeSeconds, CpuPowerLimit.MaximumTimeSeconds);

        public override string ToString() =>
            $"STAPM {Stapm / MilliwattsPerWatt:0.###} W, " +
            $"Slow {Slow / MilliwattsPerWatt:0.###} W, Fast {Fast / MilliwattsPerWatt:0.###} W";
    }

    internal delegate bool TryReadTable(out ulong[] table, out string? error);
    internal delegate bool TrySendCommand(uint command, uint value, out string? error);

    private CpuPowerLimitReport BuildReport(CpuPowerLimit current) => new(
        CpuPowerLimitAvailability.Available,
        null,
        current.Pl1Watts,
        current.Pl2Watts,
        _firmware.Pl1Watts,
        _firmware.Pl2Watts,
        CpuPowerLimit.MaximumWatts,
        false,
        UsesDirectSmu: true);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal readonly record struct SmuCommands(uint Stapm, uint Fast, uint Slow);
}
