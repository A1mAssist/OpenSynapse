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

    private readonly RyzenSmuSession _session;
    private readonly SmuCommands _commands;
    private readonly CpuPowerLimit _firmware;
    private bool _disposed;

    private RyzenSmuCpuPowerLimit(
        RyzenSmuSession session,
        SmuCommands commands,
        CpuPowerLimit firmware)
    {
        _session = session;
        _commands = commands;
        _firmware = firmware;
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

            controller = new RyzenSmuCpuPowerLimit(session, commands.Value, firmware);
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
        return _session.TryReadPowerTable(out var table, out var error) &&
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
        return RestoreTo(_firmware.Pl1Watts, _firmware.Pl2Watts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
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

        var fast = FloatAt(table, 2) / MilliwattsPerWatt;
        var slow = FloatAt(table, 4) / MilliwattsPerWatt;
        if (!(fast > 0 && slow > 0 && fast < 500 && slow < 500))
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

    private CpuPowerLimitReport Write(CpuPowerLimit limit)
    {
        var slow = ToMilliwatts(limit.Pl1Watts);
        var fast = ToMilliwatts(limit.Pl2Watts);
        if (!_session.TrySend(_commands.Stapm, slow, out var error) ||
            !_session.TrySend(_commands.Slow, slow, out error) ||
            !_session.TrySend(_commands.Fast, fast, out error))
        {
            return CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error);
        }

        // A rejected message is the only reliable failure signal; when the table cannot
        // be re-read, report what was accepted instead of failing a write that worked.
        return _session.TryReadPowerTable(out var table, out _) &&
            TryDecode(table, out var actual, out _)
                ? BuildReport(actual)
                : BuildReport(limit);
    }

    private CpuPowerLimitReport BuildReport(CpuPowerLimit current) => new(
        CpuPowerLimitAvailability.Available,
        null,
        current.Pl1Watts,
        current.Pl2Watts,
        _firmware.Pl1Watts,
        _firmware.Pl2Watts,
        CpuPowerLimit.MaximumWatts,
        false);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal readonly record struct SmuCommands(uint Stapm, uint Fast, uint Slow);
}
