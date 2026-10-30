using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public interface ICurveOptimizerController : IDisposable
{
    bool TryApply(CpuCurveOptimizer offset, out string? error);
}

/// <summary>
/// PBO Curve Optimizer rides the same MP1 mailbox as the package wall: message 0x4C on
/// the mobile families with a documented command, 0x55 on Renoir/Lucienne. The SMU has no
/// read-back for the offset, so callers record what they wrote instead of claiming a
/// firmware readback. Availability reuses the SMU probe enum because the failures are
/// identical (PawnIO missing, module rejected, access denied).
/// </summary>
public sealed class RyzenSmuCurveOptimizer : ICurveOptimizerController
{
    private const ulong Renoir = 1;
    private const ulong Rembrandt = 10;
    private const ulong Cezanne = 13;
    private const ulong Lucienne = 22;
    private const ulong Phoenix = 23;
    private const ulong Phoenix2 = 24;
    private const ulong HawkPoint = 30;
    private const ulong StrixPoint = 31;

    private readonly RyzenSmuSession _session;
    private readonly uint _message;

    private RyzenSmuCurveOptimizer(RyzenSmuSession session, uint message)
    {
        _session = session;
        _message = message;
    }

    public static CpuPowerLimitAvailability TryOpen(
        out ICurveOptimizerController? controller,
        out string? detail)
    {
        controller = null;
        var availability = RyzenSmuSession.TryOpen(out var session, out detail);
        if (session is null)
        {
            return availability;
        }

        if (MessageFor(session.CodeName) is not { } message)
        {
            session.Dispose();
            detail = "This Ryzen family does not use the supported Curve Optimizer message.";
            return CpuPowerLimitAvailability.Failed;
        }

        controller = new RyzenSmuCurveOptimizer(session, message);
        detail = null;
        return CpuPowerLimitAvailability.Available;
    }

    public bool TryApply(CpuCurveOptimizer offset, out string? error) =>
        _session.TrySend(_message, offset.ToSmuArgument(), out error);

    public void Dispose() => _session.Dispose();

    internal static uint? MessageFor(ulong codeName) => codeName switch
    {
        Renoir or Lucienne => 0x55,
        Rembrandt or Cezanne or Phoenix or Phoenix2 or HawkPoint or StrixPoint => 0x4C,
        _ => null,
    };
}
