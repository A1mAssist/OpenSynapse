using System.Xml.Linq;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class GpuCardResourceTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void OverclockCardHasLocalizedTextAndAccessibleInputNames(string language)
    {
        var card = XDocument.Load(Path.Combine(RepoLayout.Root,
            "src/OpenSynapse.App/Views/BladeGpuOverclockCard.xaml"));
        var resources = XDocument.Load(Path.Combine(RepoLayout.Root,
            $"src/OpenSynapse.App/Strings/{language}/Resources.resw"))
            .Descendants("data").ToDictionary(
                item => (string)item.Attribute("name")!,
                item => (string?)item.Element("value"));
        XNamespace local = "using:OpenSynapse.App";
        var controls = card.Descendants().Where(element =>
            element.Name.LocalName is "TextBlock" or "Button" or "ComboBox" or "NumberBox").ToArray();
        Assert.Equal(6, controls.Count(element => element.Name.LocalName is "ComboBox" or "NumberBox"));
        foreach (var control in controls)
        {
            if (control.Attribute("Text")?.Value.StartsWith("{Binding", StringComparison.Ordinal) == true)
            {
                continue;
            }
            var uid = (string?)control.Attribute(local + "Localized.Uid");
            Assert.False(string.IsNullOrWhiteSpace(uid));
            Assert.False(resources.ContainsKey(uid!),
                $"{language}: {uid} cannot be both a resource value and a property scope");
            var property = control.Name.LocalName switch
            {
                "TextBlock" => "Text",
                "Button" => "Content",
                _ => "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
            };
            var key = $"{uid}.{property}";
            Assert.True(resources.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
                $"{language}: missing resource {key}");
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void GpuModeRecoveryExplainsThePendingWriteRestartRequirement(string language)
    {
        var resources = XDocument.Load(Path.Combine(RepoLayout.Root,
            $"src/OpenSynapse.App/Strings/{language}/Resources.resw"));
        Assert.False(string.IsNullOrWhiteSpace(resources.Descendants("data")
            .Single(item => (string?)item.Attribute("name") == "GpuModeStatusPendingRestart")
            .Element("value")?.Value));
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root,
            "src/OpenSynapse.App/Views/DevicesPage.GpuMode.cs"));
        Assert.Contains("DedicatedGpuOperationUncertainException.PendingError", source);
        Assert.Contains("AppStrings.Text(\"GpuModeStatusPendingRestart\")", source);
    }

    [Fact]
    public void ThemeUsesRazerGreenAccentAndKeepsTheExistingDarkCardHierarchy()
    {
        var theme = XDocument.Load(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/App.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var brushes = theme.Descendants().Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(element => (string)element.Attribute(x + "Key")!,
                element => (string?)element.Attribute("Color"));
        var colors = theme.Descendants().Where(element => element.Name.LocalName == "Color")
            .ToDictionary(element => (string)element.Attribute(x + "Key")!,
                element => (string?)element.Value);
        Assert.Equal("#44D62C", brushes["AccentBrush"]);
        Assert.Equal("#5BE547", brushes["AccentHoverBrush"]);
        Assert.Equal("#36B522", brushes["AccentPressedBrush"]);
        Assert.Equal("#FF44D62C", colors["SystemAccentColor"]);
        Assert.Equal("#FF36B522", colors["SystemAccentColorDark1"]);
        Assert.Equal("#99DD72", brushes["OnlineBrush"]);
        Assert.Equal("#141414", brushes["CardBrush"]);
        Assert.Equal("#171717", brushes["CanvasBrush"]);
    }
}
