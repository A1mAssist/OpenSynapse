using System.Text;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioKeyboardVisualTests
{
    private const string Svg = """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" width="900" height="340" viewBox="0 0 900 340">
          <style>
            .productfill { fill: #101010; }
            .characters { fill: #d8d8d8; }
            .led { fill: #000000; }
          </style>
          <path id="shell" class="productfill" d="M0,0H900V340H0Z"/>
          <path id="lettering" class="characters" d="M10,10H20V20H10Z"/>
          <g id="US_Selection">
            <path id="selA" class="selection" data-assumed-key-name="A" data-selection-group="letters" d="M115,170.1H134V191H115Z"/>
          </g>
          <path id="keyA" class="led keys" data-assumed-key-name="A" data-selection-group="letters" d="M117,177H151V211H117Z"/>
          <path id="thsLogo" class="characters" data-assumed-key-name="Logo" data-selection-group="logo" d="M1,1H8V8H1Z"/>
          <path id="logoSelection" class="selection" data-assumed-key-name="Logo" data-selection-group="logo" d="M1,1H8V8H1Z"/>
        </svg>
        """;

    [Fact]
    public void CharactersBecomeLetteringAndLedReplacesSelectionDuplicate()
    {
        var visual = StudioKeyboardVisualBuilder.Build(Svg);

        Assert.Equal(900, visual.Width);
        Assert.Equal(340, visual.Height);
        Assert.Equal(StudioSvgRole.Lettering, visual.Paths.Single(path => path.Id == "lettering").Role);
        Assert.Equal(StudioSvgRole.Lettering, visual.Paths.Single(path => path.Id == "thsLogo").Role);

        var key = Assert.Single(visual.Keys);
        Assert.Equal("A", key.KeyName);
        Assert.Equal("keyA", key.PathId);
        Assert.Equal("letters", key.SelectionGroup);

        Assert.Contains(visual.Paths, path => path.Id == "shell");
        Assert.Contains(visual.Paths, path => path.Id == "lettering");
        Assert.Contains(visual.Paths, path => path.Id == "thsLogo");
        Assert.Contains(visual.Paths, path => path.Id == "logoSelection");
        Assert.DoesNotContain(visual.Keys, key => key.KeyName.Equals("Logo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ManifestBuildDecodesSvgPayload()
    {
        var visual = StudioKeyboardVisualBuilder.Build(Manifest(EncodeSvg(Svg)));

        Assert.NotNull(visual);
        Assert.Equal(900, visual.Width);
        Assert.Equal(340, visual.Height);
        Assert.Single(visual.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    public void EmptyOrInvalidBase64ReturnsNull(string base64)
    {
        Assert.Null(StudioKeyboardVisualBuilder.Build(Manifest(base64)));
    }

    [Fact]
    public void MalformedSvgPayloadReturnsNull()
    {
        Assert.Null(StudioKeyboardVisualBuilder.Build(Manifest(EncodeSvg("<svg><g></svg>"))));
        Assert.Null(StudioKeyboardVisualBuilder.Build(Manifest(EncodeSvg("<html><body/></html>"))));
    }

    private static StudioManifestDevice Manifest(string svgBase64) =>
        new(590, 0, "BlackWidow V3", true, "svg_0_1", svgBase64, "{}", null, null);

    private static string EncodeSvg(string svg) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
}
