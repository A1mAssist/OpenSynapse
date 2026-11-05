using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerLedTableProtocolTests
{
    [Fact]
    public void ColorTableUsesTheEightyByteTupleAndZeroPads()
    {
        var get = RazerLedTableProtocol.CreateGetColorTableRequest(0x05, 0x03, 0x24);
        Assert.Equal((0x50, 0x03, 0x8E), Command(get));
        Assert.Equal(new byte[] { 0x03, 0x24 }, get[9..11]);
        Assert.All(get[11..89], value => Assert.Equal(0, value));

        var set = RazerLedTableProtocol.CreateSetColorTableRequest(
            0x06,
            new RazerLedColorTable(0x03, 0x24, 3, [0x11, 0x22, 0x33]));

        Assert.Equal((0x50, 0x03, 0x0E), Command(set));
        Assert.Equal(new byte[] { 0x03, 0x24, 0x03, 0x11, 0x22, 0x33 }, set[9..15]);
        Assert.All(set[15..89], value => Assert.Equal(0, value));
    }

    [Fact]
    public void ColorTableParsesClassIdCountAndTheOpaqueTail()
    {
        var request = RazerLedTableProtocol.CreateGetColorTableRequest(0x05, 0x03, 0x24);
        var arguments = new byte[80];
        arguments[0] = 0x03;
        arguments[1] = 0x24;
        arguments[2] = 3;
        arguments[3] = 0x11;
        arguments[4] = 0x22;
        arguments[5] = 0x33;

        var table = RazerLedTableProtocol.ParseColorTable(
            Response(0x05, 80, 0x03, 0x8E, arguments),
            request);

        Assert.Equal(0x03, table.LedClass);
        Assert.Equal(0x24, table.LedId);
        Assert.Equal(3, table.ColorCount);
        Assert.Equal(77, table.ColorPayload.Length);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, table.ColorPayload[..3]);
        Assert.All(table.ColorPayload[3..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void RgbMaxUsesTheSixByteTupleInBothDirections()
    {
        var get = RazerLedTableProtocol.CreateGetRgbMaxRequest(0x05, 0x03, 0x24);
        Assert.Equal((0x06, 0x03, 0x88), Command(get));
        Assert.Equal(new byte[] { 0x03, 0x24 }, get[9..11]);

        var value = new RazerLedRgbMaxValue(0x03, 0x24, 0xFF, 0xE0, 0xC0, 0x80);
        var set = RazerLedTableProtocol.CreateSetRgbMaxRequest(0x06, value);
        Assert.Equal((0x06, 0x03, 0x08), Command(set));
        Assert.Equal(new byte[] { 0x03, 0x24, 0xFF, 0xE0, 0xC0, 0x80 }, set[9..15]);

        var parsed = RazerLedTableProtocol.ParseRgbMax(
            Response(0x06, 6, 0x03, 0x08, [0x03, 0x24, 0xFF, 0xE0, 0xC0, 0x80]),
            set);

        Assert.Equal(value, parsed);
    }

    [Fact]
    public void SleepLedStateUsesTheFourByteTuple()
    {
        var state = new RazerSleepLedState(0x03, 0x24, 0x01, 0x0A);
        var request = RazerLedTableProtocol.CreateSetSleepLedStateRequest(0x05, state);

        Assert.Equal((0x04, 0x03, 0x21), Command(request));
        Assert.Equal(new byte[] { 0x03, 0x24, 0x01, 0x0A }, request[9..13]);

        var parsed = RazerLedTableProtocol.ParseSleepLedState(
            Response(0x05, 4, 0x03, 0x21, [0x03, 0x24, 0x01, 0x0A]),
            request);

        Assert.Equal(state, parsed);
    }

    [Fact]
    public void ChromaStripeGetUsesTheVariablePayloadSize()
    {
        var request = RazerLedTableProtocol.CreateGetChromaStripeLedNumberRequest(0x05, 2);

        Assert.Equal((0x05, 0x0F, 0x88), Command(request));
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00 }, request[9..14]);
    }

    [Fact]
    public void ChromaStripeSetPrefixesTheCountAndKeepsTheDataBytes()
    {
        var request = RazerLedTableProtocol.CreateSetChromaStripeLedNumberRequest(
            0x06,
            2,
            [0x01, 0x03, 0x02, 0x07]);

        Assert.Equal((0x05, 0x0F, 0x08), Command(request));
        Assert.Equal(new byte[] { 0x02, 0x01, 0x03, 0x02, 0x07 }, request[9..14]);
    }

    [Fact]
    public void ChromaStripeSetZeroPadsShortDataToThePairCount()
    {
        var request = RazerLedTableProtocol.CreateSetChromaStripeLedNumberRequest(
            0x06,
            2,
            [0x01, 0x03]);

        Assert.Equal((0x05, 0x0F, 0x08), Command(request));
        Assert.Equal(new byte[] { 0x02, 0x01, 0x03, 0x00, 0x00 }, request[9..14]);
    }

    [Fact]
    public void ChromaStripeResponseMapsPairIndexToRegionAndReadsTheSecondByte()
    {
        var request = RazerLedTableProtocol.CreateGetChromaStripeLedNumberRequest(0x07, 2);
        var entries = RazerLedTableProtocol.ParseChromaStripeLedNumbers(
            Response(0x07, 5, 0x0F, 0x88, [0x02, 0x00, 0x04, 0x00, 0x09]),
            request);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new RazerChromaStripeLedNumber(0, 26, 0x04), entries[0]);
        Assert.Equal(new RazerChromaStripeLedNumber(1, 27, 0x09), entries[1]);
    }

    [Fact]
    public void TruncatedOrOutOfOrderResponsesAreRejected()
    {
        var colorRequest = RazerLedTableProtocol.CreateGetColorTableRequest(0x05, 0x03, 0x24);
        var rgbRequest = RazerLedTableProtocol.CreateGetRgbMaxRequest(0x05, 0x03, 0x24);
        var sleepRequest = RazerLedTableProtocol.CreateSetSleepLedStateRequest(
            0x05,
            new RazerSleepLedState(0x03, 0x24, 1, 2));
        var stripeRequest = RazerLedTableProtocol.CreateGetChromaStripeLedNumberRequest(0x05, 2);

        Assert.Throws<InvalidOperationException>(
            () => RazerLedTableProtocol.ParseColorTable(new byte[20], colorRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerLedTableProtocol.ParseColorTable(
                Response(0x05, 79, 0x03, 0x8E, new byte[79]),
                colorRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerLedTableProtocol.ParseRgbMax(new byte[20], rgbRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerLedTableProtocol.ParseSleepLedState(
                Response(0x05, 4, 0x03, 0x08, new byte[4]),
                sleepRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerLedTableProtocol.ParseChromaStripeLedNumbers(
            Response(0x05, 5, 0x0F, 0x88, [0x03, 0x00, 0x04, 0x00, 0x09]),
                stripeRequest));
    }

    [Fact]
    public void BuildersRejectPayloadsBeyondTheReportCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerLedTableProtocol.CreateSetColorTableRequest(
                0x05,
                new RazerLedColorTable(
                    0x03,
                    0x24,
                    0,
                    new byte[RazerLedTableProtocol.ColorPayloadSize + 1])));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerLedTableProtocol.CreateGetChromaStripeLedNumberRequest(
                0x05,
                RazerLedTableProtocol.MaxChromaStripeCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerLedTableProtocol.CreateSetChromaStripeLedNumberRequest(
                0x05,
                2,
                new byte[5]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerLedTableProtocol.CreateSetChromaStripeLedNumberRequest(
                0x05,
                RazerLedTableProtocol.MaxChromaStripeCount + 1,
                new byte[0]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerLedTableProtocol.CreateSetChromaStripeLedNumberRequest(
                0x05,
                RazerLedTableProtocol.MaxChromaStripeCount,
                new byte[RazerLedTableProtocol.MaxChromaStripeDataLength + 1]));
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(
        byte transaction,
        byte dataSize,
        byte commandClass,
        byte commandId,
        byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = commandClass;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
