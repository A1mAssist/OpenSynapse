using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>One vendor's package power wall. Both backends return the same report shape.</summary>
public interface ICpuPowerLimitController : IDisposable
{
    CpuPowerLimitReport Read();

    CpuPowerLimitReport Apply(CpuPowerLimit limit);

    CpuPowerLimitReport RestoreTo(double pl1Watts, double pl2Watts);

    CpuPowerLimitReport RestoreFirmware();
}
