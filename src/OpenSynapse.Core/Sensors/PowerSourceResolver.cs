using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Sensors;

public static class PowerSourceResolver
{
    public static PowerSourceKind Resolve(
        bool? isPluggedIn,
        BladeAdapterPowerSnapshot? adapterPower)
    {
        if (isPluggedIn == false)
        {
            return PowerSourceKind.Battery;
        }

        if (adapterPower is { ActualWatts: 0 })
        {
            return PowerSourceKind.Battery;
        }

        if (adapterPower is { ActualWatts: > 0, RecommendedWatts: > 0 })
        {
            return adapterPower.IsLimitedAdapter
                ? PowerSourceKind.LowPowerAdapter
                : PowerSourceKind.FullPowerAdapter;
        }

        // AC status can change before a fresh adapter query arrives.
        if (isPluggedIn == true)
        {
            return PowerSourceKind.FullPowerAdapter;
        }

        return PowerSourceKind.Unknown;
    }
}
