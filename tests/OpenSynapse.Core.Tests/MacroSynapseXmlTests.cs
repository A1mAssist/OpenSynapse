using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroSynapseXmlTests
{
    [Fact]
    public async Task Synapse3ScanCodesBecomeVirtualKeys()
    {
        // Real Synapse 3 export: Makecode 57 is the space bar scan code and
        // 29 is left control, neither of which is a valid virtual key.
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Keys</Name>
              <MacroEvents>
                <MacroEvent><Type>1</Type><KeyEvent><Makecode>57</Makecode></KeyEvent></MacroEvent>
                <MacroEvent><Type>1</Type><Delay>10</Delay><KeyEvent><Makecode>57</Makecode><State>1</State></KeyEvent></MacroEvent>
                <MacroEvent><Type>1</Type><Delay>5</Delay><KeyEvent><Makecode>29</Makecode></KeyEvent></MacroEvent>
                <MacroEvent><Type>1</Type><Delay>5</Delay><KeyEvent><Makecode>29</Makecode><State>1</State></KeyEvent></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal("Keys", macro.Name);
        Assert.Collection(
            macro.Steps,
            step =>
            {
                Assert.Equal(MacroStepKind.Input, step.Kind);
                Assert.True(step.IsDown);
                Assert.Equal(0x20, step.Input!.VirtualKey);
                Assert.Equal(0x39, step.Input.ScanCode);
            },
            step =>
            {
                Assert.Equal(MacroStepKind.Delay, step.Kind);
                Assert.Equal(10, step.DelayMilliseconds);
            },
            step =>
            {
                Assert.Equal(MacroStepKind.Input, step.Kind);
                Assert.False(step.IsDown);
                Assert.Equal(0x20, step.Input!.VirtualKey);
            },
            step =>
            {
                Assert.Equal(MacroStepKind.Delay, step.Kind);
                Assert.Equal(5, step.DelayMilliseconds);
            },
            step =>
            {
                Assert.Equal(MacroStepKind.Input, step.Kind);
                Assert.Equal(0xA2, step.Input!.VirtualKey);
                Assert.Equal(0x1D, step.Input.ScanCode);
            },
            step => Assert.Equal(MacroStepKind.Delay, step.Kind),
            step =>
            {
                Assert.Equal(MacroStepKind.Input, step.Kind);
                Assert.False(step.IsDown);
                Assert.Equal(0xA2, step.Input!.VirtualKey);
            });
    }

    [Fact]
    public async Task Synapse4VirtualKeysAndScanCodeAreKept()
    {
        // Real Synapse 4 export: Makecode 16 is the virtual key for shift,
        // ScanCode 42 the physical left shift.
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Keys</Name>
              <MacroEvents>
                <MacroEvent>
                  <Type>1</Type>
                  <KeyEvent><Makecode>16</Makecode><State>0</State></KeyEvent>
                  <ScanCode>42</ScanCode>
                </MacroEvent>
                <MacroEvent>
                  <Type>1</Type>
                  <KeyEvent><Makecode>16</Makecode><State>1</State></KeyEvent>
                  <ScanCode>42</ScanCode>
                </MacroEvent>
              </MacroEvents>
              <Version>4</Version>
            </Macro>
            """);

        Assert.Collection(
            macro.Steps,
            step =>
            {
                Assert.True(step.IsDown);
                Assert.Equal(0x10, step.Input!.VirtualKey);
                Assert.Equal(42, step.Input.ScanCode);
            },
            step => Assert.False(step.IsDown));
    }

    [Fact]
    public async Task Synapse3ModifiersKeepTheirPhysicalSide()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Modifiers</Name>
              <MacroEvents>
                <MacroEvent><Type>1</Type><KeyEvent><Makecode>42</Makecode></KeyEvent></MacroEvent>
                <MacroEvent><Type>1</Type><KeyEvent><Makecode>54</Makecode></KeyEvent></MacroEvent>
                <MacroEvent><Type>1</Type><KeyEvent><Makecode>29</Makecode></KeyEvent></MacroEvent>
                <MacroEvent><Type>1</Type><KeyEvent><Makecode>285</Makecode></KeyEvent></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal(0xA0, macro.Steps[0].Input!.VirtualKey);
        Assert.Equal(0xA1, macro.Steps[1].Input!.VirtualKey);
        Assert.Equal(0xA2, macro.Steps[2].Input!.VirtualKey);
        Assert.Equal(0xA3, macro.Steps[3].Input!.VirtualKey);
    }

    [Fact]
    public async Task EmptyKeyEventKeepsItsDelayInsteadOfFailing()
    {
        // Real Synapse 3 export writes an empty KeyEvent for "release every
        // held key"; the delay is the part the macro depends on.
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Release</Name>
              <MacroEvents>
                <MacroEvent><Type>1</Type><Delay>300</Delay><KeyEvent /></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        var step = Assert.Single(macro.Steps);
        Assert.Equal(MacroStepKind.Delay, step.Kind);
        Assert.Equal(300, step.DelayMilliseconds);
    }

    [Fact]
    public async Task Synapse3MouseButtonsUseOneBasedEncoding()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Mouse</Name>
              <MacroEvents>
                <MacroEvent><Type>2</Type><MouseEvent><MouseButton>2</MouseButton><State>0</State></MouseEvent></MacroEvent>
                <MacroEvent><Type>2</Type><Delay>1</Delay><MouseEvent><MouseButton>2</MouseButton><State>1</State></MouseEvent></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal(2, macro.Steps[0].Input!.MouseButton);
        Assert.True(macro.Steps[0].IsDown);
        Assert.False(macro.Steps[2].IsDown);
    }

    [Fact]
    public async Task Synapse4MouseButtonsUseZeroBasedEncoding()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Mouse</Name>
              <MacroEvents>
                <MacroEvent><Type>2</Type><MouseEvent><MouseButton>0</MouseButton><State>0</State></MouseEvent></MacroEvent>
                <MacroEvent><Type>2</Type><MouseEvent><MouseButton>1</MouseButton><State>0</State></MouseEvent></MacroEvent>
              </MacroEvents>
              <Version>4</Version>
            </Macro>
            """);

        Assert.Equal(1, macro.Steps[0].Input!.MouseButton);
        Assert.Equal(2, macro.Steps[1].Input!.MouseButton);
    }

    [Fact]
    public async Task Synapse3MouseMovementBecomesOnePathStep()
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
    public async Task Synapse4MouseMovementUsesBufferEntries()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Movement</Name>
              <MacroEvents>
                <MacroEvent>
                  <Type>3</Type>
                  <Number>0.050</Number>
                  <MouseEvent>
                    <Buffer><x>100</x><y>200</y><time>18</time></Buffer>
                    <Buffer><x>110</x><y>220</y><time>19</time></Buffer>
                  </MouseEvent>
                </MacroEvent>
              </MacroEvents>
              <MouseMoveType>relative</MouseMoveType>
              <Version>4</Version>
            </Macro>
            """);

        var step = Assert.Single(macro.Steps);
        Assert.Equal(MacroStepKind.MouseMove, step.Kind);
        Assert.Equal(MacroMouseMoveMode.Relative, step.MouseMoveMode);
        Assert.Collection(
            step.MousePath,
            point => Assert.Equal((100, 200, 18), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((110, 220, 37), (point.X, point.Y, point.OffsetMilliseconds)));
    }

    [Fact]
    public async Task Synapse3LoopMarkersBecomeOneLoopStep()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Loop</Name>
              <MacroEvents>
                <MacroEvent><Type>2</Type><MouseEvent><MouseButton>2</MouseButton><State>0</State></MouseEvent></MacroEvent>
                <MacroEvent><Type>6</Type><Loop><Count>30</Count></Loop></MacroEvent>
                <MacroEvent><Type>1</Type><Delay>150</Delay><KeyEvent><Makecode>29</Makecode></KeyEvent></MacroEvent>
                <MacroEvent><Type>6</Type><Loop><Type>1</Type><Count>30</Count></Loop></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Collection(
            macro.Steps,
            step => Assert.Equal(MacroStepKind.Input, step.Kind),
            step =>
            {
                Assert.Equal(MacroStepKind.Loop, step.Kind);
                Assert.Equal(30, step.LoopCount);
                Assert.Collection(
                    step.Children,
                    child =>
                    {
                        Assert.Equal(MacroStepKind.Delay, child.Kind);
                        Assert.Equal(150, child.DelayMilliseconds);
                    },
                    child =>
                    {
                        Assert.Equal(MacroStepKind.Input, child.Kind);
                        Assert.Equal(0xA2, child.Input!.VirtualKey);
                    });
            });
    }

    [Fact]
    public async Task Synapse4LoopBoundaryUsesState()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Loop</Name>
              <MacroEvents>
                <MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
                <MacroEvent><Type>1</Type><KeyEvent><Makecode>57</Makecode><State>0</State></KeyEvent></MacroEvent>
                <MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
              </MacroEvents>
              <Version>4</Version>
            </Macro>
            """);

        var step = Assert.Single(macro.Steps);
        Assert.Equal(MacroStepKind.Loop, step.Kind);
        Assert.Equal(5, step.LoopCount);
    }

    [Fact]
    public async Task SecondsDelayBecomesMilliseconds()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Delay</Name>
              <MacroEvents>
                <MacroEvent><Type>0</Type><Number>0.036</Number></MacroEvent>
                <MacroEvent><Type>0</Type><Number>0.5</Number></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal(36, macro.Steps[0].DelayMilliseconds);
        Assert.Equal(500, macro.Steps[1].DelayMilliseconds);
    }

    [Fact]
    public async Task Type6MillisecondDelayIsSupported()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Delay</Name>
              <MacroEvents>
                <MacroEvent><Type>2</Type><MouseEvent><MouseButton>1</MouseButton><State>0</State></MouseEvent></MacroEvent>
                <MacroEvent><Type>6</Type><Delay>265</Delay></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Equal(MacroStepKind.Delay, macro.Steps[1].Kind);
        Assert.Equal(265, macro.Steps[1].DelayMilliseconds);
    }

    [Fact]
    public async Task EditorMetadataIsIgnored()
    {
        var macro = await ImportAsync(
            """
            <Macro>
              <Name>Metadata</Name>
              <MacroEvents>
                <MacroEvent><Type>actionBar</Type><recordProfile><mmtSetting>3</mmtSetting></recordProfile></MacroEvent>
                <MacroEvent><Type>2</Type><MouseEvent><MouseButton>1</MouseButton><State>0</State></MouseEvent></MacroEvent>
              </MacroEvents>
            </Macro>
            """);

        Assert.Single(macro.Steps);
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


