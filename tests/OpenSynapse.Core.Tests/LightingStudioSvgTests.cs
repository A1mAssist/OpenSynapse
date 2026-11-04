using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenSynapse.Core.Lighting;
using Xunit;
using Xunit.Abstractions;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioSvgTests
{
    private const string CatalogFolder = "artifacts/chroma-studio-2026-11-03/catalog/manifests";
    private const string FixtureManifest = "DeviceManifest_736_0.json";
    private const string FixtureSvgKey = "svg_0_1";

    private static readonly Regex LedClassedPath = new(
        "<path[^>]*class=\"[^\"]*led",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ITestOutputHelper _output;

    public LightingStudioSvgTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SyntheticManifestResolvesRolesStylesAndKeyCoordinates()
    {
        const string svg = """
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" width="640px" height="480px" viewBox="0 0 640 480">
              <defs>
                <style>
                  .cls-a { fill: #f0f; opacity: 0.5; }
                  .cls-b, .cls-c { fill: none; stroke: #44d62c; }
                </style>
                <clipPath id="clip"><path id="clipShape" d="M0,0h1Z"/></clipPath>
              </defs>
              <g id="Product">
                <path id="shell" class="cls-c" d="M0,0h10v10H0Z"/>
              </g>
              <g id="US_LED">
                <path id="keyA" class="cls-a led" d="M1,2h3V5H1Z" data-row="0" data-col="0" data-assumed-key-name="A" data-selection-group="letters"/>
                <g data-slot="inner">
                  <path id="keyC" class="cls-a led" d="M2,2h1V3H2Z" data-row="0" data-col="2"/>
                </g>
                <path id="keyB" class="cls-a led" d="M6,7h3V9H6Z" data-row="0" data-col="1"/>
              </g>
              <g id="US_Selection">
                <path id="selA" class="cls-b selection" d="M1,2h3V5H1Z" data-row="0" data-col="0"/>
              </g>
              <text x="1" y="2">ignored</text>
              <rect id="box" x="0" y="0" width="1" height="1"/>
              <g transform="translate(10,10)"><path id="moved" d="M0,0h1Z"/></g>
            </svg>
            """;

        var layout = StudioSvgLayoutParser.Parse(svg);

        Assert.Equal(640, layout.Width);
        Assert.Equal(480, layout.Height);
        Assert.Equal(7, layout.Paths.Count);
        Assert.DoesNotContain(layout.Paths, path => path.Id is "clipShape" or "text");
        Assert.Equal("M0,0H1V1H0Z", layout.Paths.Single(path => path.Id == "box").Data);

        var shell = layout.Paths.Single(path => path.Id == "shell");
        Assert.Equal(StudioSvgRole.Product, shell.Role);
        Assert.Null(shell.Fill);
        Assert.Equal("#44d62c", shell.Stroke);
        Assert.Equal(1d, shell.Opacity);

        var keyA = layout.Paths.Single(path => path.Id == "keyA");
        Assert.Equal(StudioSvgRole.Led, keyA.Role);
        Assert.Equal("#ff00ff", keyA.Fill);
        Assert.Equal(0.5d, keyA.Opacity);
        Assert.Equal(0, keyA.Row);
        Assert.Equal(0, keyA.Col);
        Assert.Equal("A", keyA.KeyName);
        Assert.Equal("letters", keyA.SelectionGroup);
        Assert.Equal("M1,2h3V5H1Z", keyA.Data);

        Assert.Equal(StudioSvgRole.Led, layout.Paths.Single(path => path.Id == "keyC").Role);

        var keyB = layout.Paths.Single(path => path.Id == "keyB");
        Assert.Equal(StudioSvgRole.Led, keyB.Role);
        Assert.Equal(1, keyB.Col);
        Assert.Null(keyB.KeyName);
        Assert.Equal("M6,7h3V9H6Z", keyB.Data);

        var selA = layout.Paths.Single(path => path.Id == "selA");
        Assert.Equal(StudioSvgRole.Selection, selA.Role);
        Assert.Null(selA.Fill);
        Assert.Equal("#44d62c", selA.Stroke);

        Assert.Equal(StudioSvgRole.Other, layout.Paths.Single(path => path.Id == "moved").Role);
    }

    [Fact]
    public void InvalidPayloadsThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => StudioSvgLayoutParser.Parse(" "));
        Assert.Throws<ArgumentException>(() => StudioSvgLayoutParser.Parse("<svg><g></svg>"));
        Assert.Throws<ArgumentException>(() => StudioSvgLayoutParser.Parse("<html><body/></html>"));
    }

    [Fact]
    public void MissingSizeFallsBackToViewBoxAndInlineStyleWins()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 200">
              <g id="US_LED">
                <path id="inline" fill="#111" style="fill:#44d62c" d="M0,0h1Z"/>
              </g>
              <path id="threeDigit" class="led" style="fill:#0f0" opacity="0.25" d="M0,0h1Z"/>
            </svg>
            """;

        var layout = StudioSvgLayoutParser.Parse(svg);

        Assert.Equal(320, layout.Width);
        Assert.Equal(200, layout.Height);
        Assert.Equal("#44d62c", layout.Paths.Single(path => path.Id == "inline").Fill);

        var threeDigit = layout.Paths.Single(path => path.Id == "threeDigit");
        Assert.Equal("#00ff00", threeDigit.Fill);
        Assert.Equal(0.25d, threeDigit.Opacity);
        Assert.Equal(StudioSvgRole.Led, threeDigit.Role);
    }

    [Fact]
    public void RealManifestKeepsDeviceExtentAndKeyboardGrid()
    {
        var manifest = Path.Combine(
            RepoLayout.Root,
            CatalogFolder.Replace('/', Path.DirectorySeparatorChar),
            FixtureManifest);
        // The full catalog is an ignored local snapshot; keep the parser covered by inline fixtures when absent.
        if (!File.Exists(manifest)) return;

        var layout = StudioSvgLayoutParser.Parse(DecodeSvgPayload(File.ReadAllText(manifest), FixtureSvgKey));

        Assert.InRange(layout.Width, 954.9, 955.1);
        Assert.InRange(layout.Height, 589.9, 590.1);

        var ledPaths = layout.Paths.Where(path => path.Role == StudioSvgRole.Led).ToList();
        Assert.True(ledPaths.Count >= 80, $"Led paths: {ledPaths.Count}");

        var escape = ledPaths.Single(path => path.Id == "keyEsc");
        Assert.Equal(0, escape.Row);
        Assert.Equal(0, escape.Col);
        Assert.Equal("Esc", escape.KeyName);
        Assert.Equal("#ff00ff", escape.Fill);
        Assert.Equal(0.5d, escape.Opacity);

        Assert.Equal(ledPaths.Count, layout.Paths.Count(path => path.Role == StudioSvgRole.Selection));
    }

    [Fact]
    public void CatalogSweepParsesEveryDecodableSvgPayload()
    {
        var folder = Path.Combine(RepoLayout.Root, CatalogFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(folder))
        {
            // xUnit 2.9 has no runtime skip; the catalog is a local artifact snapshot, so an absent copy is not a failure.
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var unexpected = new List<string>();
        var malformed = new List<string>();
        var unparseableSupported = new List<string>();
        var manifests = 0;
        var supportedManifests = 0;
        var ledManifests = 0;
        var parsedPayloads = 0;
        var emptyValues = 0;
        var nonPayloads = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "DeviceManifest*.json").Order(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(file);
            manifests++;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var supported =
                document.RootElement.TryGetProperty("isChromaStudioSupported", out var supportedElement) &&
                supportedElement.ValueKind == JsonValueKind.True;

            if (!document.RootElement.TryGetProperty("svg", out var svgElement) ||
                svgElement.ValueKind != JsonValueKind.Object)
            {
                unexpected.Add($"{fileName}: no svg object");
                continue;
            }

            var parsedHere = 0;
            var hasLed = false;
            foreach (var payload in svgElement.EnumerateObject())
            {
                var encoded = payload.Value.GetString();
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    emptyValues++;
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(encoded);
                }
                catch (FormatException)
                {
                    // A few manifests store a resource file name instead of an SVG payload.
                    nonPayloads++;
                    continue;
                }

                var svg = Encoding.UTF8.GetString(bytes);
                StudioSvgLayout layout;
                try
                {
                    layout = StudioSvgLayoutParser.Parse(svg);
                }
                catch (ArgumentException ex) when (ex.InnerException is System.Xml.XmlException)
                {
                    // The catalog ships a handful of payloads that are not well-formed XML; the contract reports those as ArgumentException.
                    malformed.Add($"{fileName}/{payload.Name}: {ex.InnerException.Message}");
                    continue;
                }

                parsedHere++;
                parsedPayloads++;
                var ledPaths = layout.Paths.Count(path => path.Role == StudioSvgRole.Led);
                hasLed |= ledPaths > 0;

                // Devices whose LED layer is drawn with rect/circle/polygon legitimately yield no Led path.
                if (ledPaths == 0 && LedClassedPath.IsMatch(svg))
                {
                    unexpected.Add($"{fileName}/{payload.Name}: led-classed paths but no Led role");
                }
            }

            if (!supported) continue;
            supportedManifests++;
            if (parsedHere == 0)
            {
                unparseableSupported.Add(fileName);
                continue;
            }

            if (hasLed) ledManifests++;
        }

        stopwatch.Stop();
        _output.WriteLine(
            $"manifests={manifests} supported={supportedManifests} withLed={ledManifests} " +
            $"parsed={parsedPayloads} malformed={malformed.Count} empty={emptyValues} nonPayload={nonPayloads} " +
            $"elapsed={stopwatch.ElapsedMilliseconds}ms");
        foreach (var entry in malformed)
        {
            _output.WriteLine($"malformed {entry}");
        }

        if (unparseableSupported.Count > 0)
        {
            _output.WriteLine($"supported without a decodable payload: {string.Join(", ", unparseableSupported)}");
        }

        Assert.True(parsedPayloads > 0, "No SVG payload was parsed.");
        Assert.True(ledManifests > 0, "No manifest produced Led paths.");
        Assert.True(unexpected.Count == 0, string.Join(Environment.NewLine, unexpected));
    }

    private static string DecodeSvgPayload(string manifestJson, string key)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var encoded = document.RootElement.GetProperty("svg").GetProperty(key).GetString();
        Assert.False(string.IsNullOrWhiteSpace(encoded));
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded!));
    }
}