using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerMappingBundleTests
{
    private const string DrawLines184 = """
        drawLine(0,this.props.buttonList[0],114,58,!0);
        drawLine(2,this.props.buttonList[2],147,88,!0);
        drawLine(5,this.props.buttonList[5],88,145,!0);
        drawLine(6,this.props.buttonList[6],88,175,!0);
        drawLine(1,this.props.buttonList[1],181,58,!1);
        drawLine(3,this.props.buttonList[3],148,75,!1);
        drawLine(4,this.props.buttonList[4],148,105,!1);
        drawLine(7,this.props.buttonList[7],148,138,!1);
        """;

    private const string Buttons184 = """
        {buttonKey:"LeftClick",inputID:"LeftClick",counter:1,buttonPos:"left",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"RightClick",inputID:"RightClick",counter:2,buttonPos:"right",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"ScrollClick",inputID:"ScrollButton",counter:3,buttonPos:"left",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"ScrollUp",inputID:"ScrollUp",counter:4,buttonPos:"right",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"ScrollDown",inputID:"ScrollDown",counter:5,buttonPos:"right",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"MouseButton5",inputID:"Button5",counter:7,buttonPos:"left",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"MouseButton4",inputID:"Button4",counter:6,buttonPos:"left",functionList:["DEFAULT","MACRO"]},
        {buttonKey:"CycleUpSensitivityStages",inputID:"DKM_SB_03",counter:8,buttonPos:"left",functionList:["DEFAULT","MACRO"]}
        """;

    [Fact]
    public void Parses184StyleAnchorsInImageSpace()
    {
        var imageUrl = "./static/media/prd-3x.test.avif";

        var bundle = RazerMappingBundleParser.Parse(Bundle(DrawLines184, Buttons184), imageUrl);

        Assert.Equal(imageUrl, bundle.Image3x);
        Assert.Equal(300, bundle.ImageWidth);
        Assert.Equal(340, bundle.ImageHeight);
        Assert.Equal(8, bundle.MouseAnchors.Count);
        Assert.Equal(
            new byte[] { 1, 2, 3, 9, 10, 5, 4, 96 },
            bundle.MouseAnchors.Select(anchor => anchor.ButtonId));

        var leftClick = bundle.MouseAnchors.First(anchor => anchor.ButtonId == 1);
        Assert.Equal(116.5, leftClick.X, 6);
        Assert.Equal(60.5, leftClick.Y, 6);
        Assert.True(leftClick.IsLeft);

        var rightClick = bundle.MouseAnchors.First(anchor => anchor.ButtonId == 2);
        Assert.Equal(183.5, rightClick.X, 6);
        Assert.Equal(60.5, rightClick.Y, 6);
        Assert.False(rightClick.IsLeft);

        var mouseButton4 = bundle.MouseAnchors.First(anchor => anchor.ButtonId == 4);
        Assert.Equal(90.5, mouseButton4.X, 6);
        Assert.Equal(177.5, mouseButton4.Y, 6);
        Assert.True(mouseButton4.IsLeft);

        Assert.Equal("Left", bundle.ButtonNamesById[1]);
        Assert.Equal("Right", bundle.ButtonNamesById[2]);
        Assert.Equal("Middle", bundle.ButtonNamesById[3]);
        Assert.Equal("Button 4", bundle.ButtonNamesById[4]);
        Assert.Equal("Button 5", bundle.ButtonNamesById[5]);
        Assert.Equal("Scroll up", bundle.ButtonNamesById[9]);
        Assert.Equal("Scroll down", bundle.ButtonNamesById[10]);
        Assert.Equal("DPI cycle up", bundle.ButtonNamesById[96]);
    }

    [Fact]
    public void FirmwareButtonIdsFollowStandardMouseSemantics()
    {
        const string lines = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            drawLine(1,this.props.buttonList[1],30,40,!0);
            drawLine(2,this.props.buttonList[2],50,60,!0);
            """;
        const string buttons = """
            {buttonKey:"ScrollUp",counter:4},
            {buttonKey:"MouseButton4",counter:6},
            {buttonKey:"CycleUpSensitivityStages",counter:8}
            """;

        var bundle = RazerMappingBundleParser.Parse(Bundle(lines, buttons));

        Assert.Equal(
            new byte[] { 9, 4, 96 },
            bundle.MouseAnchors.Select(anchor => anchor.ButtonId));
        Assert.Equal("Scroll up", bundle.ButtonNamesById[9]);
        Assert.Equal("Button 4", bundle.ButtonNamesById[4]);
        Assert.Equal("DPI cycle up", bundle.ButtonNamesById[96]);
    }

    [Fact]
    public void DrawLineFlagMapsToSide()
    {
        const string lines = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            drawLine(1,this.props.buttonList[1],10,30,!1);
            """;
        const string buttons = """
            {buttonKey:"First",counter:1},
            {buttonKey:"Second",counter:2}
            """;

        var bundle = RazerMappingBundleParser.Parse(Bundle(lines, buttons));

        Assert.True(bundle.MouseAnchors.First(anchor => anchor.ButtonId == 1).IsLeft);
        Assert.False(bundle.MouseAnchors.First(anchor => anchor.ButtonId == 2).IsLeft);
    }

    [Fact]
    public void ButtonPosOverridesDrawLineFlag()
    {
        const string lines = """
            drawLine(0,this.props.buttonList[0],10,20,!1);
            drawLine(1,this.props.buttonList[1],10,30,!0);
            """;
        const string buttons = """
            {buttonKey:"First",counter:1,buttonPos:"left"},
            {buttonKey:"Second",counter:2,buttonPos:"right"}
            """;

        var bundle = RazerMappingBundleParser.Parse(Bundle(lines, buttons));

        Assert.True(bundle.MouseAnchors.First(anchor => anchor.ButtonId == 1).IsLeft);
        Assert.False(bundle.MouseAnchors.First(anchor => anchor.ButtonId == 2).IsLeft);
    }

    [Fact]
    public void MissingIdsProduceNoAnchor()
    {
        const string lines = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            drawLine(1,this.props.buttonList[1],10,30,!0);
            drawLine(2,this.props.buttonList[2],10,40,!0);
            drawLine(3,this.props.buttonList[3],10,50,!0);
            """;
        const string buttons = """
            {buttonKey:"NoId"},
            {buttonKey:"ZeroCounter",counter:0},
            {buttonKey:"ZeroHid",HID:"0"}
            """;

        var bundle = RazerMappingBundleParser.Parse(Bundle(lines, buttons));

        Assert.Empty(bundle.MouseAnchors);
    }

    [Fact]
    public void MissingCanvasUsesDefaultImageOffset()
    {
        const string js = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            var groupList=[{buttonKey:"First",counter:1}];
            """;

        var bundle = RazerMappingBundleParser.Parse(js);

        var anchor = Assert.Single(bundle.MouseAnchors);
        Assert.Equal(12.5, anchor.X, 6);
        Assert.Equal(22.5, anchor.Y, 6);
        Assert.Equal(300, bundle.ImageWidth);
        Assert.Equal(340, bundle.ImageHeight);
    }

    [Fact]
    public void DrawLineWithoutSideFlagDefaultsToLeft()
    {
        const string js = """
            drawLine(0,this.props.buttonList[0],10,20);
            var groupList=[{buttonKey:"First",counter:1}];
            """;

        var bundle = RazerMappingBundleParser.Parse(js);

        var anchor = Assert.Single(bundle.MouseAnchors);
        Assert.Equal(12.5, anchor.X, 6);
        Assert.Equal(22.5, anchor.Y, 6);
        Assert.True(anchor.IsLeft);
    }

    [Fact]
    public void UnknownButtonKeyIsKeptAsItsName()
    {
        const string js = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            var groupList=[{buttonKey:"CustomDial",counter:9}];
            """;

        var bundle = RazerMappingBundleParser.Parse(js);

        Assert.Equal("CustomDial", bundle.ButtonNamesById[9]);
    }

    [Fact]
    public void DotRadiusOffsetsTheHotspotCenter()
    {
        const string js = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            var groupList=[{buttonKey:"First",counter:1}];
            const h5={cvsW:310,cvsH:340,dotRadius:5};
            """;

        var bundle = RazerMappingBundleParser.Parse(js);

        var anchor = Assert.Single(bundle.MouseAnchors);
        Assert.Equal(20, anchor.X, 6);
        Assert.Equal(25, anchor.Y, 6);
    }

    [Fact]
    public void CanvasHeightCentersTheImageVertically()
    {
        const string js = """
            drawLine(0,this.props.buttonList[0],10,20,!0);
            var groupList=[{buttonKey:"First",counter:1}];
            const h5={cvsW:310,cvsH:360,dotRadius:2.5};
            """;

        var bundle = RazerMappingBundleParser.Parse(js);

        var anchor = Assert.Single(bundle.MouseAnchors);
        Assert.Equal(12.5, anchor.X, 6);
        Assert.Equal(12.5, anchor.Y, 6);
    }

    [Fact]
    public void KeyCatalogAndButtonsMapNamesToHid()
    {
        const string js = """
            const keys=[
                {name:"A",inputID:"KEY_A",type:"Alphanumeric",scancode:parseInt("1E",16),HID:"4"},
                {name:"B",inputID:"KEY_B",type:"Alphanumeric",HID:"5"}
            ];
            var groupList=[
                {buttonKey:"KeyA",inputID:"KEY_A",counter:"A",HID:"4",d:"M0 0"},
                {buttonKey:"Macro1",counter:9,HID:"30"}
            ];
            """;

        var bundle = RazerMappingBundleParser.Parse(js);

        Assert.Equal((byte)4, bundle.KeyIdsByName["A"]);
        Assert.Equal((byte)4, bundle.KeyIdsByName["KEY_A"]);
        Assert.Equal((byte)5, bundle.KeyIdsByName["B"]);
        Assert.Equal((byte)5, bundle.KeyIdsByName["KEY_B"]);
        Assert.Equal((byte)30, bundle.KeyIdsByName["Macro1"]);
        Assert.DoesNotContain("KeyA", bundle.KeyIdsByName.Keys);
    }

    [Fact]
    public void ManifestHelpersPickMainJsAndThreeXImage()
    {
        const string json = """
            {
              "files": {
                "main.js": ["./static/js/main.abc123.js"],
                "static/media/prd-3x.def456.avif": "./static/media/prd-3x.def456.avif",
                "static/media/prd-1x.def456.avif": "./static/media/prd-1x.def456.avif"
              }
            }
            """;

        Assert.Equal("./static/js/main.abc123.js", RazerMappingBundleParser.FindMainJs(json));
        Assert.Equal(
            "./static/media/prd-3x.def456.avif",
            RazerMappingBundleParser.FindImage3x(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("drawLine(")]
    [InlineData("{buttonKey:")]
    [InlineData("drawLine(0,this.props.buttonList[0],1,2,!0)")]
    public void InvalidInputDoesNotThrow(string js)
    {
        var bundle = RazerMappingBundleParser.Parse(js);

        Assert.Empty(bundle.MouseAnchors);
        Assert.Equal(300, bundle.ImageWidth);
        Assert.Equal(340, bundle.ImageHeight);
    }

    [Fact]
    public void ManifestHelpersReturnNullForInvalidInput()
    {
        Assert.Null(RazerMappingBundleParser.FindMainJs("{"));
        Assert.Null(RazerMappingBundleParser.FindImage3x("not json"));
    }

    private static string Bundle(string drawLines, string buttons) =>
        $$"""
          const h5={cvsH:340,cvsW:310,dotRadius:2.5};
          {{drawLines}}
          var groupList=[{{buttons}}];
          """;
}