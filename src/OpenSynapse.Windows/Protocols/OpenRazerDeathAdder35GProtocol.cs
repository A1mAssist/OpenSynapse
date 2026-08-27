namespace OpenSynapse.Windows.Protocols;

[Flags]
internal enum OpenRazerDeathAdder35GLeds : byte
{
    None = 0,
    Logo = 0x01,
    ScrollWheel = 0x02,
}

internal readonly record struct OpenRazerDeathAdder35GState(
    byte Poll,
    byte Dpi,
    byte Profile,
    OpenRazerDeathAdder35GLeds Leds)
{
    internal static OpenRazerDeathAdder35GState Default => new(1, 1, 1,
        OpenRazerDeathAdder35GLeds.Logo | OpenRazerDeathAdder35GLeds.ScrollWheel);

    internal int PollingRate => Poll switch
    {
        1 => 1000,
        2 => 500,
        3 => 125,
        _ => 0,
    };

    internal int DpiValue => Dpi switch
    {
        1 => 3500,
        2 => 1800,
        3 => 900,
        4 => 450,
        _ => 0,
    };
}

internal static class OpenRazerDeathAdder35GProtocol
{
    internal const int ReportLength = 4;
    internal const byte RequestType = 0x21;
    internal const byte Request = 0x09;
    internal const ushort Value = 0x0010;
    internal const ushort Index = 0x0000;

    internal static byte[] Encode(OpenRazerDeathAdder35GState state) =>
        [state.Poll, state.Dpi, state.Profile, (byte)state.Leds];

    internal static OpenRazerDeathAdder35GState Decode(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength)
        {
            throw new ArgumentException("DeathAdder 3.5G state must contain exactly four bytes.", nameof(report));
        }
        return new(report[0], report[1], report[2], (OpenRazerDeathAdder35GLeds)report[3]);
    }

    internal static OpenRazerDeathAdder35GState WithPollingRate(
        OpenRazerDeathAdder35GState state,
        int pollingRate) => state with
        {
            Poll = pollingRate switch
            {
                1000 => 1,
                500 => 2,
                125 => 3,
                _ => 2,
            },
        };

    internal static OpenRazerDeathAdder35GState WithDpi(
        OpenRazerDeathAdder35GState state,
        int dpi) => state with
        {
            Dpi = dpi switch
            {
                450 => 4,
                900 => 3,
                1800 => 2,
                _ => 1,
            },
        };

    internal static OpenRazerDeathAdder35GState WithLed(
        OpenRazerDeathAdder35GState state,
        OpenRazerDeathAdder35GLeds led,
        bool enabled)
    {
        if (led is not OpenRazerDeathAdder35GLeds.Logo and not OpenRazerDeathAdder35GLeds.ScrollWheel)
        {
            throw new ArgumentOutOfRangeException(nameof(led));
        }
        return state with { Leds = enabled ? state.Leds | led : state.Leds & ~led };
    }
}
