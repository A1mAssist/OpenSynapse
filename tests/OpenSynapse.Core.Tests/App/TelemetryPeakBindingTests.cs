using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed partial class TelemetryPeakBindingTests
{
    private static readonly string[] PeakProperties =
    [
        "CpuTemperaturePeakText",
        "CpuPowerPeakText",
        "CpuClockPeakText",
        "GpuTemperaturePeakText",
        "GpuPowerPeakText",
        "GpuClockPeakText",
    ];

    [Fact]
    public void EveryPeakTooltipIsBoundInTheTelemetryCard()
    {
        var xaml = ReadRepositoryFile(Path.Combine("src", "OpenSynapse.App", "Views", "OverviewPage.xaml"));

        foreach (var property in PeakProperties)
        {
            Assert.Contains($"ToolTipService.ToolTip=\"{{Binding Telemetry.{property}}}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryPeakTooltipIsExposedByTheTelemetryWorkspace()
    {
        var sources = ReadRepositoryFile(Path.Combine(
            "src", "OpenSynapse.App", "ViewModels", "TelemetryWorkspace.cs"));

        foreach (var property in PeakProperties)
        {
            Assert.True(
                sources.Contains($"public string {property} =>", StringComparison.Ordinal),
                $"OverviewPage.xaml binds {property} but TelemetryWorkspace does not expose it.");
        }
    }

    [Theory]
    [InlineData("en-US", "TelemetryPeakRange", 2)]
    [InlineData("zh-CN", "TelemetryPeakRange", 2)]
    public void PeakResourcePlaceholdersMatchTheFormattedArguments(string locale, string key, int expectedPlaceholders)
    {
        var value = ReadResourceValue(locale, key);
        var placeholders = Placeholder().Matches(value).Select(match => match.Value).Distinct().ToArray();

        Assert.Equal(expectedPlaceholders, placeholders.Length);
    }

    [Theory]
    [InlineData("en-US", "Min 59.3°C / Max 61°C while running")]
    [InlineData("zh-CN", "本机运行期间 最小 59.3°C / 最大 61°C")]
    public void PeakResourceFormatsTheTooltipText(string locale, string expected)
    {
        var value = ReadResourceValue(locale, "TelemetryPeakRange");
        var formatted = string.Format(value, "59.3°C", "61°C");

        Assert.Equal(expected, formatted);
    }

    private static string ReadResourceValue(string locale, string key)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSynapse.App",
            "Strings",
            locale,
            "Resources.resw");
        var element = XDocument.Load(path)
            .Root?
            .Elements("data")
            .FirstOrDefault(item => string.Equals((string?)item.Attribute("name"), key, StringComparison.Ordinal));
        Assert.NotNull(element);
        return element!.Element("value")?.Value ?? string.Empty;
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenSynapse.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the OpenSynapse repository root.");
    }

    [GeneratedRegex(@"\{\d+[^}]*\}")]
    private static partial Regex Placeholder();
}
