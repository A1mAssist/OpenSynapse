using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ViewModelAsyncContractTests
{
    [Fact]
    public void ViperSettingSavesRestoreOnlyTheirOwnProfileFields()
    {
        var source = Source("ViperWorkspace.Persistence.cs");

        Assert.Contains("finally { if (!saved) settings.PollingRateHertz = previous; }", source);
        Assert.Contains("settings.DpiX = previousX;", source);
        Assert.Contains("settings.DpiY = previousY;", source);
        Assert.Contains("finally { if (!saved) settings.IdleSeconds = previous; }", source);
        Assert.Contains("finally { if (!saved) settings.BatteryChemistry = previous; }", source);
        Assert.Equal(4, source.Split("var settings = Profile.Global.Viper;").Length - 1);
        Assert.Equal(4, source.Split("ReferenceEquals(settings, Profile.Global.Viper)").Length - 1);
        Assert.Equal(4, source.Split("if (!saved) throw ProfileSaveFailed();").Length - 1);
        Assert.DoesNotContain("_setProfile(", source);
    }

    [Fact]
    public void HapticScrollContinuationsStayOnTheCallingContext()
    {
        Assert.DoesNotContain("ConfigureAwait(false)",
            Source("RazerHidDeviceViewModel.HapticScroll.cs"));
    }

    [Fact]
    public void SoftwareLightingObservableStateStaysOnTheCallingContext() =>
        Assert.DoesNotContain("ConfigureAwait(false)", MethodBody(
            Source("RazerHidDeviceViewModel.Lighting.cs"),
            "private async Task ApplySoftwareLightingAsync"));

    private static string Source(string fileName) => File.ReadAllText(Path.Combine(
        RepoLayout.Root, "src", "OpenSynapse.App", "ViewModels", fileName));

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method: {signature}");
        var openingBrace = source.IndexOf('{', start);
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            depth += source[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0) return source[openingBrace..(index + 1)];
        }
        throw new InvalidOperationException($"Unbalanced method: {signature}");
    }
}
