using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    public async Task<bool> MigrateKeypadMacroTriggersAsync(
        IReadOnlyList<(ushort ProductId, string InstanceId)> connectedDevices, CancellationToken cancellationToken = default)
    {
        await _macroSaveGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var profiles = (_getDocument?.Invoke()?.Profiles.Values ?? Enumerable.Empty<ProfileDefinition>())
                .Append(_getActiveProfile()).Distinct();
            return await RazerKeypadMacroMigration.MigrateDevicesAsync(
                profiles, connectedDevices, _saveProfileAsync, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnosticLog.TryWrite("macro", $"keypad trigger migration failed: {exception.Message}");
            return false;
        }
        finally
        {
            _macroSaveGate.Release();
            foreach (var row in Items) row.Refresh();
            RefreshMacroRuntime();
        }
    }

    public async Task<KeypadMacroBindingTransaction> BeginKeypadMacroTransactionAsync(CancellationToken cancellationToken)
    {
        if (IsMacroRecording || IsMacroCountdown)
            throw new InvalidOperationException(AppStrings.Text("MacroEditBusy"));
        await _macroSaveGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var profile = _getActiveProfile();
            var snapshots = new Dictionary<string, (MacroDefinition? Macro, MacroBindingSnapshot? State, int SystemIndex, bool Dirty)>(StringComparer.Ordinal);
            IReadOnlyDictionary<string, long> savedVersions = new Dictionary<string, long>();
            // The runtime holds definition references; freeze them until the single profile save succeeds.
            _macroRuntime.UpdateMacros(profile.Macros.Concat(SystemShortcuts).Select(macro =>
                RazerKeypadMacroMigration.ForRuntime(MacroDocumentService.Clone(macro))));
            return new KeypadMacroBindingTransaction(
                macroId =>
                {
                    if (!ReferenceEquals(profile, _getActiveProfile()))
                        throw new InvalidOperationException(AppStrings.Text("MacroSaveFailed"));
                    if (snapshots.ContainsKey(macroId)) return;
                    var macro = profile.Macros.Concat(profile.SystemMacros).FirstOrDefault(item => item.Id == macroId);
                    snapshots.Add(macroId, (macro, macro is null ? null : new MacroBindingSnapshot(macro),
                        macro is null ? -1 : profile.SystemMacros.IndexOf(macro), _dirtyMacroIds.Contains(macroId)));
                },
                (id, input, mode, playback, repeat, layer, binding, token) => TryApplyMacroBindingCoreAsync(
                    id, input, mode, playback, repeat, layer, binding, token, deferSave: true),
                (id, input, layer, token) => TryClearMacroTriggerCoreAsync(id, input, layer, token, deferSave: true),
                () =>
                {
                    if (!ReferenceEquals(profile, _getActiveProfile()))
                        throw new InvalidOperationException(AppStrings.Text("MacroSaveFailed"));
                    if (!PrepareMacrosForSave(Items.Where(row => snapshots.ContainsKey(row.Id)).ToArray()))
                        throw new InvalidOperationException(StatusText);
                    savedVersions = snapshots.Keys.Where(_dirtyMacroIds.Contains)
                        .ToDictionary(id => id, GetMacroEditVersion, StringComparer.Ordinal);
                },
                () =>
                {
                    MarkMacroChangesSaved(savedVersions);
                    RefreshMacroRuntime();
                    NotifySelectedMacroChanged();
                },
                () =>
                {
                    foreach (var (id, snapshot) in snapshots)
                    {
                        if (snapshot.Macro is null)
                        {
                            profile.SystemMacros.RemoveAll(macro => macro.Id == id);
                        }
                        else
                        {
                            snapshot.State!.Restore();
                            if (snapshot.SystemIndex >= 0)
                            {
                                profile.SystemMacros.RemoveAll(macro => macro.Id == id);
                                profile.SystemMacros.Insert(Math.Min(snapshot.SystemIndex, profile.SystemMacros.Count), snapshot.Macro);
                            }
                        }
                        RestoreMacroDirtyState(id, snapshot.Dirty);
                        Items.FirstOrDefault(row => row.Id == id)?.Refresh();
                    }
                    RefreshMacroRuntime();
                    NotifySelectedMacroChanged();
                },
                () => _macroSaveGate.Release());
        }
        catch
        {
            _macroSaveGate.Release();
            throw;
        }
    }
}
