namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// One class 0x03 RGB maximum response: led class, led id and the four byte maxima.
/// </summary>
public readonly record struct RazerLedRgbMaxValue(
    byte LedClass,
    byte LedId,
    byte MaxRed,
    byte MaxGreen,
    byte MaxBlue,
    byte MaxBrightness);

/// <summary>
/// The 80-byte class 0x03 color-table payload. The first three bytes are led class,
/// led id and the middleware's numOfColors; the remaining 77 bytes are passed through
/// byte-for-byte and zero-padded by the sender.
/// </summary>
/// <remarks>
/// The middleware does not encode an index or bit depth in this command:
/// <c>let s=[n,e,r,...a]; s.length&lt;i.yc[0]&amp;&amp;(s=s.concat(...new Uint8Array(i.yc[0]-s.length)))</c>.
/// Its getter labels <c>data[2]</c> <c>numOfColors</c> but slices
/// <c>colors:t.data.slice(3,t.data[2]+1)</c>, which is shorter than a count of RGB
/// entries; this type therefore keeps the whole 77-byte tail instead of reproducing
/// that slice.
/// </remarks>
public sealed record RazerLedColorTable(
    byte LedClass,
    byte LedId,
    byte ColorCount,
    byte[] ColorPayload);

/// <summary>
/// Four-byte class 0x03 sleep LED payload: led class, led id, on/off and duration.
/// The middleware validates only that all four values are numbers.
/// </summary>
public readonly record struct RazerSleepLedState(
    byte LedClass,
    byte LedId,
    byte LedOn,
    byte LedOnDuration);

/// <summary>
/// One entry of the class 0x0F chroma stripe response. The middleware maps pair
/// index <c>i</c> to region <c>26 + i</c> and reads the LED number from the pair's
/// second byte.
/// </summary>
public readonly record struct RazerChromaStripeLedNumber(
    int Index,
    int RegionId,
    byte LedNumber);

/// <summary>
/// Protocol 25 keyboard LED detail commands from the 028D BlackWidow V4 Pro,
/// 02A6 Huntsman V3 Pro and 02B3 BlackWidow V4 Pro 75% bundles.
/// </summary>
/// <remarks>
/// Command table and wrapper snippets from <c>rzDevice25</c>:
/// <c>Y=new Uint8Array([80,3,142])</c> / <c>Q=new Uint8Array([80,3,14])</c> for the
/// color table, <c>j=new Uint8Array([6,3,136])</c> / <c>$=new Uint8Array([6,3,8])</c>
/// for the RGB maxima, <c>yt=(new Uint8Array([4,3,161]),new Uint8Array([4,3,33]))</c>
/// for sleep, and <c>I=new Uint8Array([1,15,144])</c> / <c>N=new Uint8Array([1,15,16])</c>
/// for the chroma stripe count.
/// The same table also declares unused three-byte color-table pairs
/// <c>X=[3,3,143]</c> and <c>tt=[3,3,15]</c>; the wrappers resolve their exports to
/// the 80-byte tuples above, so those are the bytes sent.
/// The nearby <c>b=[80,3,137]</c> is the Get LED ID List tuple, not the RGB max pair;
/// the export map binds <c>k5</c>/<c>Yd</c> to the 6-byte <c>j</c>/<c>$</c>.
/// For the chroma stripe commands the table's first byte is only a hint: the wrapper
/// passes <c>1+2*n</c> as <c>packetSizeSet</c>, and <c>_createDataSend</c> writes that
/// value into the report's data-size byte before copying the payload.
/// </remarks>
public static class RazerLedTableProtocol
{
    public const byte LedCommandClass = 0x03;
    public const byte ChromaCommandClass = 0x0F;

    public const int ColorTableArgumentSize = 80;
    public const int ColorTableHeaderSize = 3;
    public const int ColorPayloadSize = ColorTableArgumentSize - ColorTableHeaderSize;

    public const int RgbMaxArgumentSize = 6;
    public const int SleepArgumentSize = 4;

    public const int ChromaStripeCountSize = 1;
    public const int ChromaStripePairSize = 2;
    public const int ChromaStripeRegionIdBase = 26;
    public const int MaxChromaStripeCount = 39;
    public const int MaxChromaStripeDataLength = ChromaStripePairSize * MaxChromaStripeCount;

    private const byte GetColorTableCommand = 0x8E;
    private const byte SetColorTableCommand = 0x0E;
    private const byte GetRgbMaxCommand = 0x88;
    private const byte SetRgbMaxCommand = 0x08;
    private const byte SetSleepLedStateCommand = 0x21;
    // 0F:88 / 0F:08 are the stripe LED count. 0F:90 / 0F:10 are Force Chroma Ctrl on the
    // same class; the earlier values here were those commands, not the stripe count.
    private const byte GetChromaStripeCommand = 0x88;
    private const byte SetChromaStripeCommand = 0x08;

    public static byte[] CreateGetColorTableRequest(byte transactionId, byte ledClass, byte ledId)
    {
        Span<byte> arguments = new byte[ColorTableArgumentSize];
        arguments[0] = ledClass;
        arguments[1] = ledId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ColorTableArgumentSize,
            LedCommandClass,
            GetColorTableCommand,
            arguments);
    }

    public static byte[] CreateSetColorTableRequest(byte transactionId, RazerLedColorTable table)
    {
        if (table.ColorPayload.Length > ColorPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(table),
                table.ColorPayload.Length,
                $"The color table carries at most {ColorPayloadSize} payload bytes.");
        }

        Span<byte> arguments = new byte[ColorTableArgumentSize];
        arguments[0] = table.LedClass;
        arguments[1] = table.LedId;
        arguments[2] = table.ColorCount;
        table.ColorPayload.CopyTo(arguments[ColorTableHeaderSize..]);
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ColorTableArgumentSize,
            LedCommandClass,
            SetColorTableCommand,
            arguments);
    }

    public static RazerLedColorTable ParseColorTable(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var data = FixedArguments(response, request, ColorTableArgumentSize, "LED color table");
        return new RazerLedColorTable(
            data[0],
            data[1],
            data[2],
            data[ColorTableHeaderSize..].ToArray());
    }

    public static byte[] CreateGetRgbMaxRequest(byte transactionId, byte ledClass, byte ledId)
    {
        Span<byte> arguments = new byte[RgbMaxArgumentSize];
        arguments[0] = ledClass;
        arguments[1] = ledId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            RgbMaxArgumentSize,
            LedCommandClass,
            GetRgbMaxCommand,
            arguments);
    }

    public static byte[] CreateSetRgbMaxRequest(byte transactionId, RazerLedRgbMaxValue value)
    {
        Span<byte> arguments = new byte[RgbMaxArgumentSize];
        arguments[0] = value.LedClass;
        arguments[1] = value.LedId;
        arguments[2] = value.MaxRed;
        arguments[3] = value.MaxGreen;
        arguments[4] = value.MaxBlue;
        arguments[5] = value.MaxBrightness;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            RgbMaxArgumentSize,
            LedCommandClass,
            SetRgbMaxCommand,
            arguments);
    }

    public static RazerLedRgbMaxValue ParseRgbMax(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var data = FixedArguments(response, request, RgbMaxArgumentSize, "LED RGB maximum");
        return new RazerLedRgbMaxValue(data[0], data[1], data[2], data[3], data[4], data[5]);
    }

    public static byte[] CreateSetSleepLedStateRequest(
        byte transactionId,
        RazerSleepLedState state)
    {
        Span<byte> arguments = new byte[SleepArgumentSize];
        arguments[0] = state.LedClass;
        arguments[1] = state.LedId;
        arguments[2] = state.LedOn;
        arguments[3] = state.LedOnDuration;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            SleepArgumentSize,
            LedCommandClass,
            SetSleepLedStateCommand,
            arguments);
    }

    public static RazerSleepLedState ParseSleepLedState(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var data = FixedArguments(response, request, SleepArgumentSize, "Sleep LED state");
        return new RazerSleepLedState(data[0], data[1], data[2], data[3]);
    }

    /// <summary>
    /// Builds the getter with the wrapper's packetSizeSet of <c>1+2*n</c>, which
    /// becomes the report's data-size byte.
    /// </summary>
    public static byte[] CreateGetChromaStripeLedNumberRequest(
        byte transactionId,
        byte ledCount)
    {
        if (ledCount > MaxChromaStripeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ledCount),
                ledCount,
                $"A chroma stripe request carries at most {MaxChromaStripeCount} entries.");
        }

        var size = ChromaStripeCountSize + (ChromaStripePairSize * ledCount);
        Span<byte> arguments = new byte[size];
        arguments[0] = ledCount;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)size,
            ChromaCommandClass,
            GetChromaStripeCommand,
            arguments);
    }

    /// <summary>
    /// Builds the setter. The middleware sizes its buffer from the supplied data but
    /// <c>_createDataSend</c> copies only <c>1+2*n</c> bytes; this builder rejects
    /// longer data instead of silently dropping it and zero-pads shorter data.
    /// </summary>
    public static byte[] CreateSetChromaStripeLedNumberRequest(
        byte transactionId,
        byte ledCount,
        ReadOnlySpan<byte> stripeData)
    {
        if (ledCount > MaxChromaStripeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ledCount),
                ledCount,
                $"A chroma stripe request carries at most {MaxChromaStripeCount} entries.");
        }

        var dataSize = ChromaStripePairSize * ledCount;
        if (stripeData.Length > dataSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stripeData),
                stripeData.Length,
                $"A chroma stripe request with {ledCount} entries carries at most {dataSize} data bytes.");
        }

        Span<byte> arguments = new byte[ChromaStripeCountSize + dataSize];
        arguments[0] = ledCount;
        stripeData.CopyTo(arguments[1..]);
        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            ChromaCommandClass,
            SetChromaStripeCommand,
            arguments);
    }

    public static IReadOnlyList<RazerChromaStripeLedNumber> ParseChromaStripeLedNumbers(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (response.Length != RazerFeatureReport.Length ||
            !RazerFeatureReport.IsAcceptedStatus(response[1]) ||
            !RazerFeatureReport.Matches(request, response))
        {
            throw new InvalidOperationException(
                "Chroma stripe LED numbers returned an invalid or out-of-order feature report.");
        }

        var data = response.Slice(RazerFeatureReport.ArgumentsOffset, 80);
        var count = data[0];
        if (count > MaxChromaStripeCount ||
            response[6] != ChromaStripeCountSize + (ChromaStripePairSize * count))
        {
            throw new InvalidOperationException(
                $"Chroma stripe response declares {count} entries.");
        }

        var entries = new RazerChromaStripeLedNumber[count];
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = new RazerChromaStripeLedNumber(
                index,
                ChromaStripeRegionIdBase + index,
                data[2 + (2 * index)]);
        }

        return entries;
    }

    private static ReadOnlySpan<byte> FixedArguments(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte size,
        string feature)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, size))
        {
            throw new InvalidOperationException(
                $"{feature} returned an invalid or out-of-order feature report.");
        }

        return response.Slice(RazerFeatureReport.ArgumentsOffset, size);
    }
}
