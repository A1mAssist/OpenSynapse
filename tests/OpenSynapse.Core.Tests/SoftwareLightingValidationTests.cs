using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Core.Tests;

public sealed class SoftwareLightingValidationTests
{
    [Theory]
    [InlineData("wave", BladeLightingMode.Wave)]
    [InlineData("fire", BladeLightingMode.Fire)]
    [InlineData("starlight", BladeLightingMode.Starlight)]
    [InlineData("tidal", BladeLightingMode.Tidal)]
    public void ParsesSourceBackedVisualValidationModes(string value, BladeLightingMode expected)
    {
        var output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        var options = SoftwareLightingValidation.Options.Parse(
            ["--software-lighting", value, "--hold-seconds", "5", "--output", output]);

        Assert.Equal(expected, options.Mode);
        Assert.Equal(5, options.HoldSeconds);
        Assert.Equal(output, options.OutputPath);
    }
}
