using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Tests;

public sealed class ViperButtonAssignmentRowViewModelTests
{
    [Fact]
    public void UnknownActionRemainsVisibleAndCannotBeApplied()
    {
        var row = new ViperButtonAssignmentRowViewModel(new(
            1, 5, ViperButtonMappingLayer.Normal,
            ViperButtonMappingFunction.MediaKey, new byte[] { 0xB0, 0x00 }));

        Assert.Equal(-1, row.SelectedActionIndex);
        Assert.Equal("MediaKey · B000", row.CurrentActionText);
        Assert.False(row.CanApply);
        Assert.DoesNotContain("多媒体", row.ActionOptions);
    }

    [Theory]
    [InlineData(1, "左键")]
    [InlineData(2, "右键")]
    [InlineData(3, "滚轮按下")]
    [InlineData(4, "后退")]
    [InlineData(5, "前进")]
    [InlineData(9, "滚轮向上")]
    [InlineData(10, "滚轮向下")]
    [InlineData(96, "DPI 切换键")]
    public void UsesPhysicalControlNames(byte buttonId, string expected)
    {
        var row = new ViperButtonAssignmentRowViewModel(new(
            1, buttonId, ViperButtonMappingLayer.Normal,
            ViperButtonMappingFunction.MouseButton, new byte[] { buttonId }));

        Assert.Equal(expected, row.ButtonText);
        Assert.Equal(expected, row.CurrentActionText);
    }

    [Fact]
    public void KeyboardParametersParticipateInApplyAndRestore()
    {
        var original = new ViperButtonAssignment(
            1, 5, ViperButtonMappingLayer.HyperShift,
            ViperButtonMappingFunction.KeyboardKey, new byte[] { 0, 4 });
        var row = new ViperButtonAssignmentRowViewModel(original);

        Assert.False(row.CanApply);
        row.KeyboardModifierValue = 2;
        row.KeyboardUsageValue = 30;
        Assert.True(row.CanApply);
        Assert.Equal(new byte[] { 2, 30 }, row.CreateAssignment().FunctionData);

        row.RestoreSelection();
        Assert.Equal(0, row.KeyboardModifierValue);
        Assert.Equal(4, row.KeyboardUsageValue);
        Assert.False(row.CanApply);
    }

    [Fact]
    public void DoubleClickUsesTheVerifiedFixedPayload()
    {
        var row = new ViperButtonAssignmentRowViewModel(new(
            1, 5, ViperButtonMappingLayer.Normal,
            ViperButtonMappingFunction.MouseButton, new byte[] { 1 }));

        row.SelectedActionIndex = 9;
        var assignment = row.CreateAssignment();

        Assert.Equal(ViperButtonMappingFunction.DoubleClick, assignment.Function);
        Assert.Equal(new byte[] { 1 }, assignment.FunctionData);
        Assert.True(row.CanApply);
    }

    [Fact]
    public void RestoresVerifiedExtendedActionsIntoTheEditor()
    {
        var row = new ViperButtonAssignmentRowViewModel(new(
            1, 96, ViperButtonMappingLayer.Normal,
            ViperButtonMappingFunction.Dpi, new byte[] { 6 }));

        Assert.Equal(10, row.SelectedActionIndex);
        Assert.Equal("DPI 循环切换", row.CurrentActionText);
        Assert.False(row.CanApply);
    }
}
