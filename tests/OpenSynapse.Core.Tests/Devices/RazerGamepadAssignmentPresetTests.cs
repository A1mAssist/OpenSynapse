using System.Xml.Linq;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerGamepadAssignmentPresetTests
{
    [Fact]
    public void FirstPresetKeepsTheCurrentAssignment()
    {
        var keep = RazerGamepadAssignmentPresets.All[0];
        Assert.True(keep.IsUnchanged);
        Assert.Throws<InvalidOperationException>(() => keep.Encode());
    }

    [Fact]
    public void PresetsDelegateToTheExistingEncoders()
    {
        var byKey = RazerGamepadAssignmentPresets.All.ToDictionary(preset => preset.LabelKey);
        AssertFunction(byKey["RazerGamepadAssignmentOff"].Encode(), RazerGamepadFunction.Off, 0);
        AssertFunction(
            byKey["RazerGamepadAssignmentControllerA"].Encode(),
            RazerGamepadFunction.Controller, 1, (byte)RazerGamepadControllerButton.AOrCross);
        AssertFunction(
            byKey["RazerGamepadAssignmentControllerDPadRight"].Encode(),
            RazerGamepadFunction.Controller, 1, (byte)RazerGamepadControllerButton.DPadRight);
        AssertFunction(
            byKey["RazerGamepadAssignmentKeyW"].Encode(),
            RazerGamepadFunction.KeyCode, 2, 0, 0x1A);
        AssertFunction(
            byKey["RazerGamepadAssignmentKeyEnter"].Encode(),
            RazerGamepadFunction.KeyCode, 2, 0, 0x28);
    }

    [Fact]
    public void KeyPresetsUseKeyboardPageHidUsages()
    {
        var expected = new Dictionary<string, byte>
        {
            ["RazerGamepadAssignmentKeyW"] = 0x1A,
            ["RazerGamepadAssignmentKeyA"] = 0x04,
            ["RazerGamepadAssignmentKeyS"] = 0x16,
            ["RazerGamepadAssignmentKeyD"] = 0x07,
            ["RazerGamepadAssignmentKeySpace"] = 0x2C,
            ["RazerGamepadAssignmentKeyEnter"] = 0x28,
            ["RazerGamepadAssignmentKeyEscape"] = 0x29,
            ["RazerGamepadAssignmentKeyTab"] = 0x2B,
        };
        var byKey = RazerGamepadAssignmentPresets.All.ToDictionary(preset => preset.LabelKey);
        foreach (var (key, usage) in expected)
        {
            var data = byKey[key].Encode();
            Assert.Equal(RazerGamepadFunction.KeyCode, data.Function);
            Assert.Equal(usage, data.Data[1]);
        }
    }

    [Fact]
    public void EveryEditorLabelExistsInBothLocales()
    {
        var required = RazerGamepadAssignmentPresets.All
            .Select(preset => preset.LabelKey)
            .Concat(["RazerGamepadAssignmentAction.Header", "RazerGamepadAssignmentApply.Content"])
            .ToArray();
        var repository = FindRepositoryRoot();
        foreach (var locale in new[] { "en-US", "zh-CN" })
        {
            var path = Path.Combine(repository, "src", "OpenSynapse.App", "Strings", locale, "Resources.resw");
            var keys = XDocument.Load(path).Root!
                .Elements("data")
                .Select(element => (string?)element.Attribute("name"))
                .ToHashSet(StringComparer.Ordinal);
            var missing = required.Where(key => !keys.Contains(key)).ToArray();
            Assert.True(missing.Length == 0, $"{locale} missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void PresetKeysAreUnique()
    {
        Assert.Equal(
            RazerGamepadAssignmentPresets.All.Count,
            RazerGamepadAssignmentPresets.All
                .Select(preset => preset.LabelKey)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    private static void AssertFunction(
        RazerGamepadFunctionData data,
        RazerGamepadFunction function,
        byte size,
        params byte[] value)
    {
        Assert.Equal(function, data.Function);
        Assert.Equal(size, data.Size);
        Assert.Equal(value, data.Data[..size]);
    }

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
}