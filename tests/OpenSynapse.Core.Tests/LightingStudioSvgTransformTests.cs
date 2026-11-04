using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioSvgTransformTests
{
    [Fact]
    public void GroupTransformListIsBakedIntoPath()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <g transform="translate(10,20) scale(2)">
                <path id="p" d="M0,0 L5,5"/>
              </g>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M10,20L20,30", path.Data);
    }

    [Fact]
    public void NestedTransformsApplyOuterGroupFirst()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100">
              <g transform="translate(100,0)">
                <g transform="scale(2)">
                  <path id="p" d="M1,0L2,0"/>
                </g>
              </g>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M102,0L104,0", path.Data);
    }

    [Fact]
    public void ElementTransformAppliesInsideItsParent()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <g transform="translate(10,10)">
                <path id="p" transform="rotate(90)" d="M1,0L2,0"/>
              </g>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M10,11L10,12", path.Data);
    }

    [Fact]
    public void MatrixTransformUsesAllSixAffineComponents()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <path id="p" transform="matrix(2 0 0 3 5 7)" d="M0,0L1,1"/>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M5,7L7,10", path.Data);
    }

    [Fact]
    public void RelativeCommandsBecomeAbsoluteWhenTransformed()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <path id="p" transform="translate(5,5)" d="m1,1 2,0 0,2h2v3z"/>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M6,6L8,6L8,8L10,8L10,11Z", path.Data);
    }

    [Fact]
    public void SmoothCurveReflectionUsesLocalCoordinates()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <path id="p" transform="scale(2)" d="M0,0C1,0 2,0 3,0S4,0 5,0"/>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M0,0C2,0 4,0 6,0C8,0 8,0 10,0", path.Data);
    }

    [Fact]
    public void ArcUnderTransformBecomesCubicSegments()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <path id="p" transform="translate(5,5)" d="M0,0A10,10 0 0 1 10,10"/>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.StartsWith("M5,5C", path.Data, StringComparison.Ordinal);
        Assert.DoesNotContain('A', path.Data);
    }

    [Fact]
    public void BasicShapesBecomePaths()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <rect id="rect" x="1" y="2" width="4" height="6"/>
              <rect id="rounded" x="0" y="0" width="10" height="6" rx="2"/>
              <circle id="circle" cx="5" cy="5" r="2"/>
              <ellipse id="ellipse" cx="5" cy="5" rx="3" ry="2"/>
              <line id="line" x1="1" y1="2" x2="3" y2="4"/>
              <polygon id="polygon" points="0,0 2,0 1,2"/>
              <polyline id="polyline" points="0,0 1,1 2,0"/>
            </svg>
            """;

        var layout = StudioSvgLayoutParser.Parse(svg);

        Assert.Equal(7, layout.Paths.Count);
        Assert.Equal("M1,2H5V8H1Z", layout.Paths.Single(path => path.Id == "rect").Data);
        Assert.Contains('A', layout.Paths.Single(path => path.Id == "rounded").Data);
        Assert.Equal("M3,5A2,2 0 1 0 7,5A2,2 0 1 0 3,5Z",
            layout.Paths.Single(path => path.Id == "circle").Data);
        Assert.Equal("M2,5A3,2 0 1 0 8,5A3,2 0 1 0 2,5Z",
            layout.Paths.Single(path => path.Id == "ellipse").Data);
        Assert.Equal("M1,2L3,4", layout.Paths.Single(path => path.Id == "line").Data);
        Assert.Equal("M0,0L2,0L1,2Z", layout.Paths.Single(path => path.Id == "polygon").Data);
        Assert.Equal("M0,0L1,1L2,0", layout.Paths.Single(path => path.Id == "polyline").Data);
    }

    [Fact]
    public void ShapeTransformBakesIntoGeneratedPath()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <rect id="p" x="1" y="2" width="3" height="4" transform="translate(10,20)"/>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("M11,22L14,22L14,26L11,26Z", path.Data);
    }

    [Fact]
    public void ShapeKeepsCssRoleAndInheritedLedCell()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <defs><style>.lit { fill: #f0f; opacity: 0.4; }</style></defs>
              <g id="LED">
                <g data-row="2" data-col="3" data-assumed-key-name="KeyA" data-selection-group="letters">
                  <rect id="key" class="lit" x="1" y="2" width="3" height="4"/>
                </g>
              </g>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal(StudioSvgRole.Led, path.Role);
        Assert.Equal("#ff00ff", path.Fill);
        Assert.Equal(0.4d, path.Opacity);
        Assert.Equal(2, path.Row);
        Assert.Equal(3, path.Col);
        Assert.Equal("KeyA", path.KeyName);
        Assert.Equal("letters", path.SelectionGroup);
    }

    [Fact]
    public void InvalidTransformsFallBackToIdentity()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <g transform="translate(10) nope(5)">
                <path id="badFunction" d="M0,0L1,1"/>
              </g>
              <path id="badMatrix" transform="matrix(1 2 3)" d="M0,0L1,1"/>
              <g transform="translate(10,10)">
                <path id="good" d="M0,0L1,1"/>
              </g>
            </svg>
            """;

        var layout = StudioSvgLayoutParser.Parse(svg);

        Assert.Equal("M0,0L1,1", layout.Paths.Single(path => path.Id == "badFunction").Data);
        Assert.Equal("M0,0L1,1", layout.Paths.Single(path => path.Id == "badMatrix").Data);
        Assert.Equal("M10,10L11,11", layout.Paths.Single(path => path.Id == "good").Data);
    }

    [Fact]
    public void UnsupportedContainersDoNotLeakGeometry()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <mask id="mask"><rect id="masked" x="0" y="0" width="2" height="2"/></mask>
              <clipPath id="clip"><rect id="clipped" x="0" y="0" width="2" height="2"/></clipPath>
              <symbol id="symbol"><rect id="symbolShape" x="0" y="0" width="2" height="2"/></symbol>
              <text x="0" y="0"><tspan>ignored</tspan></text>
              <foreignObject><rect id="foreign" x="0" y="0" width="2" height="2"/></foreignObject>
              <use href="#symbol"/>
              <image href="missing.png"/>
              <rect id="visible" x="1" y="2" width="3" height="4"/>
            </svg>
            """;

        var path = Assert.Single(StudioSvgLayoutParser.Parse(svg).Paths);

        Assert.Equal("visible", path.Id);
        Assert.Equal("M1,2H4V6H1Z", path.Data);
    }

    [Fact]
    public void InvalidShapeGeometryIsSkipped()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <rect id="empty" x="0" y="0" width="0" height="4"/>
              <circle id="noRadius" cx="1" cy="1"/>
              <polygon id="onePoint" points="1,1"/>
              <path id="emptyPath" d=" "/>
            </svg>
            """;

        Assert.Empty(StudioSvgLayoutParser.Parse(svg).Paths);
    }
}
