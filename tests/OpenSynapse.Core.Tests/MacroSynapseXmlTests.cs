using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroSynapseXmlTests
{
    [Fact]
    public async Task KeyEventsKeepPressReleaseOrderAndDelay()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Keys</Name>
              <MacroEvents>
                <MacroEvent>
                  <Type>1</Type>
                  <Delay>25</Delay>
                  <KeyEvent><Makecode>49</Makecode><State>0</State></KeyEvent>
                  <ScanCode>2</ScanCode>
                </MacroEvent>
                <MacroEvent>
                  <Type>1</Type>
                  <KeyEvent><Makecode>49</Makecode><State>1</State></KeyEvent>
                  <ScanCode>2</ScanCode>
                </MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal("Keys", macro.Name);
        Assert.Collection(
            macro.Steps,
            step =>
            {
                Assert.Equal(MacroStepKind.Delay, step.Kind);
                Assert.Equal(25, step.DelayMilliseconds);
            },
            step =>
            {
                Assert.Equal(MacroStepKind.Input, step.Kind);
                Assert.True(step.IsDown);
                Assert.False(step.AutoRelease);
                Assert.Equal(49, step.Input!.VirtualKey);
                Assert.Equal(2, step.Input.ScanCode);
            },
            step =>
            {
                Assert.Equal(MacroStepKind.Input, step.Kind);
                Assert.False(step.IsDown);
                Assert.Equal(49, step.Input!.VirtualKey);
            });
    }

    [Fact]
    public async Task StandaloneDelayEventIsPreserved()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Delays</Name>
              <MacroEvents>
                <MacroEvent><Type>0</Type><Delay>40</Delay></MacroEvent>
                <MacroEvent>
                  <Type>2</Type>
                  <MouseEvent><MouseButton>4</MouseButton><State>0</State></MouseEvent>
                </MacroEvent>
                <MacroEvent>
                  <Type>2</Type>
                  <MouseEvent><MouseButton>4</MouseButton><State>1</State></MouseEvent>
                </MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal(MacroStepKind.Delay, macro.Steps[0].Kind);
        Assert.Equal(40, macro.Steps[0].DelayMilliseconds);
        Assert.Equal(4, macro.Steps[1].Input!.MouseButton);
        Assert.True(macro.Steps[1].IsDown);
        Assert.False(macro.Steps[2].IsDown);
    }

    [Fact]
    public async Task RightShiftUsesItsPhysicalScanCode()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Modifiers</Name>
              <MacroEvents>
                <MacroEvent><KeyEvent><Makecode>16</Makecode><State>0</State></KeyEvent><ScanCode>42</ScanCode></MacroEvent>
                <MacroEvent><KeyEvent><Makecode>16</Makecode><State>0</State></KeyEvent><ScanCode>310</ScanCode></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        var left = macro.Steps[0].Input!;
        var right = macro.Steps[1].Input!;
        Assert.Equal(0xA0, left.VirtualKey);
        Assert.Equal(42, left.ScanCode);
        Assert.Equal(0xA1, right.VirtualKey);
        Assert.Equal(54, right.ScanCode);
        Assert.False(right.Extended);
    }

    [Fact]
    public async Task MouseMovementBecomesOneAbsolutePathStep()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Movement</Name>
              <MacroEvents>
                <MacroEvent>
                  <Type>3</Type>
                  <Delay>7</Delay>
                  <MouseMovement>
                    <MouseMovementEvent><Type>3</Type><X>100</X><Y>200</Y><Delay>0</Delay></MouseMovementEvent>
                    <MouseMovementEvent><Type>3</Type><X>110</X><Y>220</Y><Delay>29</Delay></MouseMovementEvent>
                    <MouseMovementEvent><Type>3</Type><X>120</X><Y>240</Y><Delay>26</Delay></MouseMovementEvent>
                  </MouseMovement>
                </MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        var step = Assert.Single(macro.Steps);
        Assert.Equal(MacroStepKind.MouseMove, step.Kind);
        Assert.Equal(7, step.DelayMilliseconds);
        Assert.Equal(MacroMouseMoveMode.Absolute, step.MouseMoveMode);
        Assert.Collection(
            step.MousePath,
            point => Assert.Equal((100, 200, 0), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((110, 220, 29), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((120, 240, 55), (point.X, point.Y, point.OffsetMilliseconds)));
    }

    [Fact]
    public async Task UnsupportedEventIsRejectedInsteadOfSilentlyDropped()
    {
        await AssertInvalidAsync(
            """
            <Macro>
              <Name>Unknown</Name>
              <MacroEvents>
                <MacroEvent><Type>1</Type></MacroEvent>
              </MacroEvents>
            </Macro>
            """);
    }

    [Fact]
    public async Task MalformedXmlIsReportedAsInvalidData()
    {
        await AssertInvalidAsync("<Macro><MacroEvents>");
    }

    private static async Task<MacroDefinition> ImportAsync(string xml)
    {
        var path = await WriteTempFileAsync(xml);
        try
        {
            return Assert.Single(await MacroFileStore.ImportBundleAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task AssertInvalidAsync(string xml)
    {
        var path = await WriteTempFileAsync(xml);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => MacroFileStore.ImportBundleAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteTempFileAsync(string xml)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-synapse-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml);
        return path;
    }
}
