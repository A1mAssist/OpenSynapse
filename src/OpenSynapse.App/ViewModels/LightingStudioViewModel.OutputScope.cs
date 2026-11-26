using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

public sealed record StudioOutputChoice(string InstanceId, string Name, bool IsOnline, bool IsSelected);

public sealed partial class LightingStudioViewModel
{
    private StudioOutputScope _appliedScope = new() { Mode = StudioOutputScopeMode.Selected };
    private readonly Dictionary<string, string> _appliedNames = new(StringComparer.OrdinalIgnoreCase);
    private string? _bladeRestoreId;
    private bool _retryingRestores;

    private StudioOutputScope PendingScope => _project.SelectedProfile?.OutputScope ?? new();

    private static bool ScopeIncludes(StudioOutputScope scope, StudioDeviceOption device) =>
        device.EffectiveInstanceId is { Length: > 0 } id && scope.Includes(id);

    public IReadOnlyList<StudioOutputChoice> GetOutputChoices()
    {
        var scope = PendingScope;
        var choices = Devices.Where(device => !device.HasNoPixelOutput && device.HasMatrix &&
                !string.IsNullOrWhiteSpace(device.EffectiveInstanceId))
            .DistinctBy(device => device.EffectiveInstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(device => new StudioOutputChoice(device.EffectiveInstanceId!, device.Name,
                device.IsOutputAvailable,
                ScopeIncludes(scope, device))).ToList();
        foreach (var target in scope.Targets)
            if (!choices.Any(choice => StudioLedSelection.SameInstance(choice.InstanceId, target.InstanceId)))
                choices.Add(new StudioOutputChoice(target.InstanceId, target.Name, false, true));
        return choices;
    }

    public string PendingOutputSummary => FormatScopeSummary(
        "LightingStudioPendingOutputScope", GetOutputChoices().Where(choice => choice.IsSelected));

    public string AppliedOutputSummary => IsApplied
        ? FormatScopeSummary("LightingStudioAppliedOutputScope", _appliedNames.Select(pair =>
            new StudioOutputChoice(pair.Key, pair.Value,
                Devices.Any(device => device.IsOutputAvailable &&
                    StudioLedSelection.SameInstance(device.EffectiveInstanceId, pair.Key)), true)))
        : AppStrings.Text("LightingStudioStatusStopped");

    private static string FormatScopeSummary(string key, IEnumerable<StudioOutputChoice> choices)
    {
        var selected = choices.ToArray();
        return AppStrings.FormatText(key, selected.Length, string.Join(", ", selected.Select(choice =>
            choice.IsOnline ? choice.Name : AppStrings.FormatText("LightingStudioOutputOffline", choice.Name))));
    }

    public void SetOutputScope(StudioOutputScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_project.SelectedProfile is not { } profile) return;
        profile.OutputScope = scope.Clone();
        _worldDirty = true;
        _sceneProfile = null;
        EnsureWorld();
        _history?.Capture();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyOutputScope();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ApplyAllAsync()
    {
        SetOutputScope(new StudioOutputScope());
        await ApplyAsync();
    }

    public Task ApplyCurrentAsync() => SelectedDevice?.EffectiveInstanceId is { Length: > 0 } id
        ? ApplySelectedAsync([id]) : Task.CompletedTask;

    public async Task ApplySelectedAsync(IEnumerable<string> instanceIds)
    {
        var choices = GetOutputChoices().ToDictionary(choice => choice.InstanceId, StringComparer.OrdinalIgnoreCase);
        var scope = new StudioOutputScope { Mode = StudioOutputScopeMode.Selected };
        foreach (var id in instanceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (choices.TryGetValue(id, out var choice))
                scope.Targets.Add(new StudioOutputTarget(id, choice.Name));
        }
        SetOutputScope(scope);
        await ApplyAsync();
    }

    private void NotifyOutputScope()
    {
        if (IsApplied)
            foreach (var device in Devices.Where(device => !device.HasNoPixelOutput && device.HasMatrix &&
                         ScopeIncludes(_appliedScope, device)))
                _appliedNames[device.EffectiveInstanceId!] = device.Name;
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PendingOutputSummary));
        OnPropertyChanged(nameof(AppliedOutputSummary));
    }

    private async Task SetRouterTargetsAsync(StudioOutputScope scope)
    {
        IReadOnlyCollection<string>? ids = scope.Mode == StudioOutputScopeMode.All
            ? null : scope.Targets.Select(target => target.InstanceId).ToArray();
        async Task ChangeAsync(string name, Func<Task> change)
        {
            try { await change(); }
            catch (Exception error) when (IsExpected(error)) { OnHidRestoreFailed(name, error); }
        }
        if (_hidRouter is not null) await ChangeAsync("HID", () => _hidRouter.SetTargetsAsync(ids));
        if (_argbRouter is not null) await ChangeAsync("ARGB", () => _argbRouter.SetTargetsAsync(ids));
        if (_headsetRouter is not null) await ChangeAsync("Headset", () => _headsetRouter.SetTargetsAsync(ids));
        if (_chromaRouter is not null) await ChangeAsync("Chroma", () => _chromaRouter.SetTargetsAsync(ids));
    }

    private async Task TryReleaseBladeAsync()
    {
        try { await ReleaseBladeAsync(); }
        catch (Exception error) when (IsExpected(error))
        {
            OnHidRestoreFailed(_appliedBlade?.Name ?? "Blade", error);
        }
    }

    private async Task RetryPendingRestoresAsync()
    {
        if (_retryingRestores || _isBusy || _disposed != 0 || HasSuspendedOutput) return;
        var owned = (_hidRouter?.OwnedInstanceIds ?? [])
            .Concat(_argbRouter?.OwnedInstanceIds ?? [])
            .Concat(_headsetRouter?.OwnedInstanceIds ?? [])
            .Concat(_chromaRouter?.OwnedInstanceIds ?? []);
        if (_bladeRestoreId is null && !owned.Any(id => !_appliedScope.Includes(id))) return;
        _retryingRestores = true;
        await _outputUpdateGate.WaitAsync();
        try
        {
            if (_bladeRestoreId is not null) await TryReleaseBladeAsync();
            await SetRouterTargetsAsync(_appliedScope);
        }
        finally
        {
            _outputUpdateGate.Release();
            _retryingRestores = false;
        }
    }

    private async Task ReleaseBladeAsync()
    {
        if (_appliedBlade is { } blade)
        {
            _bladeRestoreId = blade.EffectiveInstanceId;
            if (_lighting is not null) await _lighting.StopAsync();
            _source = null;
            _appliedBlade = null;
        }
        if (_bladeRestoreId is { } id)
        {
            if (_restoreAfterExternal is { } restore) await restore([id]);
            _bladeRestoreId = null;
        }
    }

    internal Task RestoreOwnedTargetsAsync(IReadOnlyCollection<string> instanceIds)
    {
        if (_restoreAfterExternal is not { } restore) return Task.CompletedTask;
        if (_dispatcher is null || _dispatcher.HasThreadAccess) return restore(instanceIds);

        // Router retries run on the output thread; card view models belong to the UI thread.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await restore(instanceIds);
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
            }))
            completion.TrySetException(new InvalidOperationException("The lighting restore dispatcher is unavailable."));
        return completion.Task;
    }
}
