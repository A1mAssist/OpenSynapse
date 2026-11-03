using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioVideoDecodeTests
{
    private const string ReproCommand =
        "dotnet test tests\\OpenSynapse.Core.Tests\\OpenSynapse.Core.Tests.csproj " +
        "--filter FullyQualifiedName~LightingStudioVideoDecode";

    [Fact]
    public async Task RealVideoDecodesMultipleFramesAndSamplesBothHalves()
    {
        // 64x64, not 32x32: GetThumbnailAsync never completes for a 32x32
        // H.264 clip on this machine; 64x64 decodes in about 600 ms per frame.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-two-tone.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var source = await LoadAsync(path);
        Assert.True(
            source is not null,
            $"Media Foundation could not decode '{path}'. Reproduce with: {ReproCommand}");

        Assert.True(source.TrySampleAt(0.5, 0.5, 0.1, out var early), "Early sample failed.");
        Assert.True(source.TrySampleAt(0.5, 0.5, 0.85, out var late), "Late sample failed.");

        // A failed video decode falls back to one shell thumbnail frame, so two
        // different colours prove the source decoded at least two real frames.
        Assert.True(IsRed(early), $"Expected the first 0.5 s to be red, got {early}.");
        Assert.True(IsBlue(late), $"Expected the last 0.5 s to be blue, got {late}.");
    }

    private static async Task<StudioMediaSource?> LoadAsync(string path)
    {
        try
        {
            return await StudioMediaSource.TryLoadAsync(path).WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"Media Foundation did not decode '{path}' within 60 s. Reproduce with: {ReproCommand}", ex);
        }
    }

    private static bool IsRed(StudioRgb color) => color.R > 200 && color.G < 60 && color.B < 60;

    private static bool IsBlue(StudioRgb color) => color.B > 200 && color.R < 60 && color.G < 60;
}
