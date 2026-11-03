using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OpenSynapse.Core.Tests;

// Static UI contracts: these tests do not instantiate WinUI or certify rendering.
public sealed class AppLocalizationRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App"));
    private static readonly XNamespace Local = "using:OpenSynapse.App";
    private static readonly HashSet<string> TextProperties =
        ["Text", "Content", "Header", "PlaceholderText", "OnContent", "OffContent", "AutomationProperties.Name", "ToolTipService.ToolTip"];

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void EveryLocalizedStaticPropertyHasAResourceAndAnApplyBranch(string language)
    {
        var resources = Resources(language);
        var implementation = File.ReadAllText(Path.Combine(AppRoot, "Localized.cs"));
        Assert.Contains("var primary = AppStrings.TryGet(uid);", implementation);
        var errors = new List<string>();
        var checkedProperties = 0;
        foreach (var file in RepoLayout.SourceFiles().Where(path => path.StartsWith(AppRoot, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var control in XDocument.Load(file).Descendants())
            {
                var uid = (string?)control.Attribute(Local + "Localized.Uid");
                if (uid is null) continue;
                foreach (var attribute in control.Attributes().Where(attribute => TextProperties.Contains(attribute.Name.LocalName)))
                {
                    var value = attribute.Value;
                    if (string.IsNullOrEmpty(value) || value.StartsWith('{')) continue;
                    checkedProperties++;
                    var property = attribute.Name.LocalName;
                    var type = control.Name.LocalName;
                    var key = CanonicalKey(uid + "." + ResourceProperty(property));
                    var found = resources.TryGetValue(key, out var translated) ||
                        property == PrimaryProperty(type) && resources.TryGetValue(uid, out translated);
                    if (!found || string.IsNullOrWhiteSpace(translated))
                        errors.Add($"{RepoLayout.Relative(file)}: {type} {uid}/{property}");
                    Assert.True(Supported(type, property), $"Missing Localized.Apply branch: {type}.{property}");
                }
            }
        }
        Assert.Empty(errors);
        Assert.True(checkedProperties > 300, $"Expected full XAML property coverage, got {checkedProperties}");
        foreach (var branch in new[] { "MenuFlyoutSubItem", "Expander", "ToggleSwitch", "ComboBox" })
            Assert.Contains($"element is {branch}", implementation);
        Assert.Contains("comboBox.PlaceholderText = comboPlaceholder", implementation);
        Assert.Contains("toggleSwitch.Header = header", implementation);
        Assert.Contains("element is not Expander", implementation);
    }

    [Fact]
    public void LanguageKeysAreUniqueAlignedAndDoNotMixValuesWithPropertyScopes()
    {
        var en = Resources("en-US");
        var zh = Resources("zh-CN");
        Assert.Equal(en.Keys.Order(), zh.Keys.Order());
        foreach (var (key, value) in en)
        {
            Assert.False(en.ContainsKey(key.Split('/')[0]) && key.Contains('/'), $"PRI value/subtree collision: {key}");
            static string[] Parameters(string text) => Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^}]*)\}")
                .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();
            Assert.Equal(Parameters(value), Parameters(zh[key]));
        }
        Assert.Equal("The mouse setting was applied to the device, but the profile could not be saved. The in-memory profile field was restored.", en["ViperSettingAppliedProfileSaveFailed"]);
        Assert.Equal("鼠标设置已在设备上生效，但配置保存失败，已恢复内存中的对应配置字段。", zh["ViperSettingAppliedProfileSaveFailed"]);
        foreach (var key in new[] { "FirmwareUpdatePartial", "FirmwareUpdateAvailablePartial", "FirmwareUpdateComparedCurrent", "DiagnosticPreviouslyObserved" })
        {
            Assert.True(en.ContainsKey(key), $"Missing cross-owner resource: {key}");
            Assert.False(string.IsNullOrWhiteSpace(zh[key]));
        }
    }

    [Fact]
    public void NavigationHasStableNamesAndAutomationIds()
    {
        var window = XDocument.Load(Path.Combine(AppRoot, "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "MacrosNavigationItem", "ProfilesNavigationItem", "DiagnosticsNavigationItem", "SettingsNavigationItem" })
        {
            var item = Assert.Single(window.Descendants(), element => (string?)element.Attribute(x + "Name") == name);
            Assert.Equal(name, (string?)item.Attribute("AutomationProperties.AutomationId"));
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void DirectDynamicTextReferencesResolveBareOrExistingTextScope(string language)
    {
        var resources = Resources(language);
        var errors = new List<string>();
        foreach (var file in RepoLayout.SourceFiles().Where(path => path.StartsWith(AppRoot, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "AppStrings\\.(?:Text|FormatText)\\(\\s*\"([^\"]+)\"(?=\\s*[,\\)])"))
            {
                var key = CanonicalKey(match.Groups[1].Value);
                if (!resources.ContainsKey(key) && !resources.ContainsKey(key + "/Text"))
                    errors.Add($"{RepoLayout.Relative(file)}: {key}");
            }
        }
        Assert.Empty(errors);
        Assert.Contains("TryGet(key) ?? TryGet($\"{key}/Text\")", File.ReadAllText(Path.Combine(AppRoot, "AppStrings.cs")));
    }

    private static Dictionary<string, string> Resources(string language) => Resources(XDocument.Load(
        Path.Combine(AppRoot, "Strings", language, "Resources.resw")));

    private static Dictionary<string, string> Resources(XDocument document) => document.Descendants("data").ToDictionary(
            element => CanonicalKey((string)element.Attribute("name")!), element => (string?)element.Element("value") ?? "");

    // MRT treats dots and slashes as equivalent resource-scope separators.
    private static string CanonicalKey(string key) => key.Replace('.', '/');

    [Theory]
    [InlineData("AwBleHeadsetSerialLabel.Text", "AwBleHeadsetSerialLabel/Text")]
    [InlineData("Preview.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name", "Preview.[using:Microsoft.UI.Xaml.Automation]AutomationProperties/Name")]
    public void PriEquivalentAliasesCannotEscapeDuplicateDetection(string dotted, string slashed)
    {
        Assert.Equal(CanonicalKey(dotted), CanonicalKey(slashed));
        var document = XDocument.Load(Path.Combine(AppRoot, "Strings", "en-US", "Resources.resw"));
        document.Descendants("data").Where(element =>
            CanonicalKey((string)element.Attribute("name")!) == CanonicalKey(dotted)).Remove();
        document.Root!.Add(new XElement("data", new XAttribute("name", dotted), new XElement("value", "first")),
            new XElement("data", new XAttribute("name", slashed), new XElement("value", "second")));
        Assert.Throws<ArgumentException>(() => Resources(document));
    }

    private static string PrimaryProperty(string type) => type switch
    {
        "TextBlock" or "MenuFlyoutItem" or "ToggleMenuFlyoutItem" or "MenuFlyoutSubItem" => "Text",
        "NumberBox" or "ComboBox" or "ToggleSwitch" or "Expander" => "Header",
        _ => "Content",
    };

    private static string ResourceProperty(string property) => property switch
    {
        "AutomationProperties.Name" => "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        "ToolTipService.ToolTip" => "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        _ => property,
    };

    private static bool Supported(string type, string property) => property switch
    {
        "AutomationProperties.Name" or "ToolTipService.ToolTip" => true,
        "Text" => type is "TextBlock" or "MenuFlyoutItem" or "ToggleMenuFlyoutItem" or "MenuFlyoutSubItem",
        "Header" => type is "NumberBox" or "ComboBox" or "ToggleSwitch" or "Expander",
        "PlaceholderText" => type is "TextBox" or "ComboBox",
        "OnContent" or "OffContent" => type == "ToggleSwitch",
        "Content" => type is "Button" or "CheckBox" or "RadioButton" or "ComboBoxItem" or "NavigationViewItem" or "HyperlinkButton" or "DropDownButton",
        _ => false,
    };
}
