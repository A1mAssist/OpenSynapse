using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Sensors;

public static class PowerSourceResolver
{
    public readonly record struct PowerSourceEvidence(
        PowerSourceKind Kind,
        bool AdapterVerified);

    public static PowerSourceKind Resolve(
        bool? isPluggedIn,
        BladeAdapterPowerSnapshot? adapterPower) =>
        ResolveEvidence(isPluggedIn, adapterPower).Kind;

    /// <summary>
    /// Same decision as <see cref="Resolve"/>, plus whether the adapter wattage was
    /// actually read. A full adapter inferred from AC alone is not verified.
    /// </summary>
    public static PowerSourceEvidence ResolveEvidence(
        bool? isPluggedIn,
        BladeAdapterPowerSnapshot? adapterPower)
    {
        if (isPluggedIn == false)
        {
            return new(PowerSourceKind.Battery, AdapterVerified: false);
        }

        if (adapterPower is { ActualWatts: 0 })
        {
            return new(PowerSourceKind.Battery, AdapterVerified: false);
        }

        if (adapterPower is { ActualWatts: > 0, RecommendedWatts: > 0 })
        {
            return new(
                adapterPower.IsLimitedAdapter
                    ? PowerSourceKind.LowPowerAdapter
                    : PowerSourceKind.FullPowerAdapter,
                AdapterVerified: true);
        }

        // AC status can change before a fresh adapter query arrives.
        if (isPluggedIn == true)
        {
            return new(PowerSourceKind.FullPowerAdapter, AdapterVerified: false);
        }

        return new(PowerSourceKind.Unknown, AdapterVerified: false);
    }

    public static bool IsVerifiedFullPower(PowerSourceEvidence evidence) =>
        evidence.Kind == PowerSourceKind.FullPowerAdapter && evidence.AdapterVerified;
}
