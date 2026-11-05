using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioProjectModelTests
{
    [Fact]
    public void ProfileOperationsCreateRenameDuplicateDeleteAndSwitch()
    {
        var project = StudioProject.CreateDefault();
        var first = project.Profiles[0];

        var second = project.AddProfile("Second");
        Assert.Equal(2, project.Profiles.Count);
        Assert.Same(second, project.SelectedProfile);
        Assert.True(project.RenameProfile(second, "Renamed"));
        Assert.Equal("Renamed", second.Title);
        Assert.False(project.RenameProfile(new StudioProfile("Foreign"), "Nope"));

        second.Items.Add(StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave));
        var group = second.AddGroup("Group");
        group.Children.Add(StudioLayerItem.CreateLayer("Inner", StudioEffectKind.Solid));
        second.Devices.Add(new StudioDevicePlacement
        {
            InstanceId = "instance",
            ProductId = 736,
            X = 10,
            Y = 20,
            Rotation = 90,
        });

        var copy = project.DuplicateProfile(second);
        Assert.NotNull(copy);
        Assert.Equal(3, project.Profiles.Count);
        Assert.Same(copy, project.Profiles[2]);
        Assert.Same(copy, project.SelectedProfile);
        Assert.NotEqual(second.Id, copy!.Id);
        Assert.Equal("Renamed copy", copy.Title);
        Assert.NotEqual(second.Items[0].Id, copy.Items[0].Id);
        Assert.NotEqual(second.Items[1].Id, copy.Items[1].Id);
        Assert.NotEqual(group.Children[0].Id, copy.Items[0].Children[0].Id);
        Assert.Equal("instance", copy.Devices[0].InstanceId);
        Assert.Equal(90, copy.Devices[0].Rotation);

        project.SelectedProfileId = first.Id;
        Assert.Same(first, project.SelectedProfile);

        // Deleting a selected profile falls forward to the next one.
        project.SelectedProfileId = second.Id;
        Assert.Same(copy, project.RemoveProfile(second));
        Assert.Equal(copy.Id, project.SelectedProfileId);

        // Deleting the selected last profile falls back to the previous one.
        Assert.Same(first, project.RemoveProfile(copy));
        Assert.Equal(first.Id, project.SelectedProfileId);

        // The last profile is kept.
        Assert.Same(first, project.RemoveProfile(first));
        Assert.Single(project.Profiles);
        Assert.Null(project.RemoveProfile(new StudioProfile("Foreign")));
    }

    [Fact]
    public void EmptyProfileListRecoversOneDefaultProfile()
    {
        var project = StudioProjectCodec.Deserialize("""{ "profiles": [] }""");

        var profile = Assert.Single(project.Profiles);
        Assert.Equal("Default", profile.Title);
        Assert.Equal(profile.Id, project.SelectedProfileId);
    }

    [Fact]
    public void GroupOperationsValidateTargetsAndKeepTheTreeConsistent()
    {
        var project = StudioProject.CreateDefault();
        var profile = project.SelectedProfile!;
        var layer = StudioLayerItem.CreateLayer("Layer", StudioEffectKind.Solid);
        profile.Items.Add(layer);
        var group = profile.AddGroup("Group");
        var nested = StudioLayerItem.CreateGroup("Nested");
        group.Children.Add(nested);

        Assert.Same(group, profile.Items[0]);
        Assert.True(profile.MoveIntoGroup(layer, group));
        Assert.DoesNotContain(layer, profile.Items);
        Assert.Same(layer, group.Children[0]);
        Assert.True(profile.MoveIntoGroup(layer, group));

        Assert.False(profile.MoveIntoGroup(group, group));
        Assert.False(profile.MoveIntoGroup(group, nested));
        Assert.False(profile.MoveIntoGroup(nested, group));
        Assert.False(profile.MoveIntoGroup(layer, layer));
        Assert.False(profile.MoveIntoGroup(
            layer, StudioLayerItem.CreateLayer("Not a group", StudioEffectKind.Solid)));

        Assert.True(profile.MoveToRoot(layer, 0));
        Assert.Same(layer, profile.Items[0]);
        Assert.DoesNotContain(layer, group.Children);

        group.Visible = false;
        Assert.False(nested.Visible);
        Assert.True(profile.MoveIntoGroup(layer, group));
        Assert.False(layer.Visible);
        group.Visible = true;
        Assert.True(nested.Visible);
        Assert.True(layer.Visible);

        var deep = StudioLayerItem.CreateLayer("Deep", StudioEffectKind.Breathing);
        group.Children.Add(deep);
        var clone = profile.DuplicateItem(group);
        Assert.NotNull(clone);
        Assert.NotEqual(group.Id, clone!.Id);
        Assert.Equal(group.Type, clone.Type);
        Assert.Equal(group.Children.Count, clone.Children.Count);
        var sourceIds = group.EnumerateDescendants().Select(descendant => descendant.Id).ToHashSet();
        Assert.All(clone.EnumerateDescendants(), descendant => Assert.DoesNotContain(descendant.Id, sourceIds));
        Assert.Same(clone, profile.Items[profile.Items.IndexOf(group) + 1]);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var layers = document.RootElement.GetProperty("profiles")[0].GetProperty("layers");
            var groupDto = layers.EnumerateArray()
                .Single(element => element.GetProperty("id").GetString() == group.Id.ToString());
            Assert.Equal("group", groupDto.GetProperty("type").GetString());
            foreach (var child in groupDto.GetProperty("layers").EnumerateArray())
                Assert.Equal(group.Id.ToString(), child.GetProperty("groupId").GetString());
        }

        var restoredProject = StudioProjectCodec.Deserialize(json);
        var restoredProfile = restoredProject.Profiles[0];
        var restoredGroup = Assert.Single(
            restoredProfile.EnumerateLayers(), item => item.Id == group.Id);
        Assert.True(restoredGroup.IsGroup);
        Assert.Equal(group.Children.Count, restoredGroup.Children.Count);
        Assert.Single(restoredProfile.EnumerateLayers(), item => item.Id == clone.Id);
        Assert.Equal(json, StudioProjectCodec.Serialize(restoredProject));

        Assert.Same(nested, profile.RemoveItem(layer));
        Assert.Same(deep, profile.RemoveItem(nested));
        Assert.Same(group, profile.RemoveItem(deep));
        Assert.Empty(group.Children);
        Assert.Null(profile.RemoveItem(StudioLayerItem.CreateLayer("Foreign", StudioEffectKind.Solid)));

        var background = profile.Items[^1];
        Assert.Same(clone, profile.RemoveItem(background));
        Assert.Same(group, profile.RemoveItem(clone));
        Assert.Null(profile.RemoveItem(group));
        Assert.Empty(profile.Items);
    }

    [Fact]
    public void DragMovesRejectGroupsAndLeaveTheTreeUntouchedOnFailure()
    {
        var project = StudioProject.CreateDefault();
        var profile = project.SelectedProfile!;
        var group = profile.AddGroup("Group");
        var inner = StudioLayerItem.CreateGroup("Inner");
        group.Children.Add(inner);
        var layer = StudioLayerItem.CreateLayer("Layer", StudioEffectKind.Solid);
        profile.Items.Add(layer);

        // Groups never move: into themselves, into another group, or onto a layer.
        var before = StudioProjectCodec.Serialize(project);
        Assert.False(profile.MoveIntoGroup(group, group));
        Assert.False(profile.MoveIntoGroup(group, inner));
        Assert.False(profile.MoveIntoGroup(inner, group));
        Assert.False(profile.MoveIntoGroup(layer, layer));
        Assert.False(profile.MoveToRoot(
            StudioLayerItem.CreateLayer("Foreign", StudioEffectKind.Solid)));
        Assert.Equal(before, StudioProjectCodec.Serialize(project));

        // A root layer drops into a group, then back out to a chosen root slot.
        Assert.True(profile.MoveIntoGroup(layer, group));
        Assert.Same(layer, group.Children[0]);
        Assert.DoesNotContain(layer, profile.Items);

        Assert.True(profile.MoveToRoot(layer, 1));
        Assert.Same(layer, profile.Items[1]);
        Assert.DoesNotContain(layer, group.Children);
    }

    [Fact]
    public void EffectPropertyForwardsToParamsAndNotifies()
    {
        var layer = StudioLayerItem.CreateLayer("Layer", StudioEffectKind.Solid);
        var raised = new List<string?>();
        layer.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        layer.Params.Effect = StudioEffectKind.Wave;
        Assert.Equal(StudioEffectKind.Wave, layer.Effect);
        Assert.Empty(raised);

        layer.NotifyEffectChanged();
        Assert.Equal(nameof(StudioLayerItem.Effect), Assert.Single(raised));
    }

    [Fact]
    public void HistoryUndoRedoRestoresSnapshotsAndStopsAtBounds()
    {
        var project = StudioProject.CreateDefault();
        var history = new StudioHistory(project);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.Undo());
        Assert.Null(history.Redo());

        project.Name = "One";
        project.SelectedProfile!.Items[0].Title = "Changed";
        history.Capture();

        var extra = project.AddProfile("Extra");
        extra.AddGroup("Group");
        history.Capture();

        project.SelectedProfileId = project.Profiles[0].Id;
        var top = project.Profiles[0].AddGroup("Top");
        project.Profiles[0].Items.Move(0, 1);
        var wave = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        project.Profiles[0].Items.Insert(0, wave);
        history.Capture();

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        var previous = history.Undo();
        Assert.NotNull(previous);
        Assert.Equal(2, previous!.Profiles.Count);
        Assert.Equal("Extra", previous.SelectedProfile!.Title);
        Assert.Single(previous.Profiles[0].Items);
        Assert.Equal("Changed", previous.Profiles[0].Items[0].Title);

        var stepOne = history.Undo();
        Assert.NotNull(stepOne);
        Assert.Equal("One", stepOne!.Name);
        Assert.Single(stepOne.Profiles);
        Assert.Equal("Changed", stepOne.SelectedProfile!.Items[0].Title);

        var initial = history.Undo();
        Assert.NotNull(initial);
        Assert.Equal("Untitled Studio", initial!.Name);
        Assert.Null(history.Undo());

        var firstRedo = history.Redo();
        Assert.NotNull(firstRedo);
        Assert.Equal("One", firstRedo!.Name);
        var secondRedo = history.Redo();
        Assert.NotNull(secondRedo);
        Assert.Equal(2, secondRedo!.Profiles.Count);
        var thirdRedo = history.Redo();
        Assert.NotNull(thirdRedo);
        Assert.Equal(3, thirdRedo!.SelectedProfile!.Items.Count);
        Assert.Equal(wave.Id, thirdRedo.SelectedProfile.Items[0].Id);
        Assert.Equal(top.Id, thirdRedo.SelectedProfile.Items[2].Id);
        Assert.Equal(StudioEffectKind.Wave,
            thirdRedo.SelectedProfile.Items.Single(item => item.Id == wave.Id).Params.Effect);
        Assert.Null(history.Redo());

        // Capturing after an undo discards the redo branch.
        var branch = history.Undo();
        Assert.NotNull(branch);
        branch!.Name = "Branch";
        history.Capture();
        Assert.False(history.CanRedo);
        Assert.Equal("One", history.Undo()!.Name);
        Assert.Equal("Branch", history.Redo()!.Name);
    }

    [Fact]
    public void HistoryKeepsTwentyUndoSteps()
    {
        var project = StudioProject.CreateDefault();
        var history = new StudioHistory(project);
        for (var index = 0; index < 25; index++)
        {
            project.Name = "Step " + index;
            history.Capture();
        }

        var steps = 0;
        StudioProject? state = null;
        while (history.CanUndo)
        {
            state = history.Undo();
            steps++;
        }

        Assert.Equal(StudioHistory.Capacity, steps);
        Assert.Equal("Step 4", state!.Name);
        Assert.False(history.CanUndo);

        while (history.CanRedo) state = history.Redo();
        Assert.Equal("Step 24", state!.Name);
    }
}
