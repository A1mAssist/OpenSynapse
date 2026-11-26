using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    internal async Task<bool> CommitMacroEditsBeforeProfileSwitchAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsMacroCountdown)
        {
            CancelMacroCountdown();
        }

        if (IsMacroRecording &&
            !await StopMacroRecordingAsync(cancellationToken))
        {
            return false;
        }

        return !HasUnsavedMacroChanges ||
            await SaveMacroChangesCoreAsync(forceSelected: false, cancellationToken);
    }

    public async Task SaveSelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is null)
        {
            return;
        }

        CancelMacroStepCapture();
        if (!await SaveMacroChangesCoreAsync(forceSelected: true, cancellationToken))
        {
            return;
        }

        StatusText = AppStrings.Text("MacroSaved");
    }

    public async Task SaveMacroEnabledAsync(
        MacroRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_isSavingMacroEnabled)
        {
            return;
        }

        _isSavingMacroEnabled = true;
        try
        {
            var enabled = row.Enabled;
            var wasDirty = _dirtyMacroIds.Contains(row.Id);
            if (IsMacroRecording || IsMacroCountdown)
            {
                row.Enabled = !enabled;
                RestoreMacroDirtyState(row.Id, wasDirty);
                return;
            }

            MarkMacroDirty(row);
            if (!await SaveMacroChangesCoreAsync(forceSelected: false, cancellationToken))
            {
                row.Enabled = !enabled;
                RestoreMacroDirtyState(row.Id, wasDirty);
                return;
            }

            StatusText = AppStrings.Text("MacroSaved");
        }
        finally
        {
            _isSavingMacroEnabled = false;
        }
    }

    public async Task<bool> SaveMacroChangesOnCloseAsync(
        CancellationToken cancellationToken = default)
    {
        if (!HasUnsavedMacroChanges)
        {
            return true;
        }

        return await SaveMacroChangesCoreAsync(forceSelected: false, cancellationToken);
    }

    private async Task<bool> SaveMacroChangesCoreAsync(
        bool forceSelected,
        CancellationToken cancellationToken)
    {
        var rows = Items
            .Where(row => _dirtyMacroIds.Contains(row.Id) ||
                (forceSelected && ReferenceEquals(row, SelectedMacro)))
            .Distinct()
            .ToList();
        if (rows.Count == 0)
        {
            return true;
        }

        if (!PrepareMacrosForSave(rows))
        {
            return false;
        }

        if (!await TrySaveMacroProfileAsync(cancellationToken, prepareDirtyMacros: false))
        {
            return false;
        }

        foreach (var row in rows)
        {
            row.Refresh();
        }

        if (SelectedMacro is not null)
        {
            RefreshMacroSteps(selectStep: SelectedStep?.Step);
        }

        RefreshMacroRuntime();
        return true;
    }

    public async Task ExportSelectedMacroAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        if (!ValidateMacroTree(selected.Definition))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            return;
        }

        if (!ValidateMacroReferences(selected.Definition))
        {
            StatusText = AppStrings.Text("MacroReferenceInvalid");
            return;
        }

        try
        {
            await MacroDocumentService.ExportAsync(
                selected.Definition,
                ActiveMacroMap(),
                filePath,
                cancellationToken);
            StatusText = AppStrings.FormatText(
                "MacroExported",
                Path.GetFileName(filePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException)
        {
            StatusText = AppStrings.FormatText("MacroExportFailed", exception.Message);
        }
    }

    public async Task ImportMacroAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(filePath);
        StatusText = AppStrings.FormatText("MacroImporting", fileName);
        IReadOnlyList<MacroDefinition> imported;
        try
        {
            imported = await MacroDocumentService.ImportAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText = AppStrings.FormatText("MacroImportFailed", fileName, exception.Message);
            return;
        }

        var profile = _getActiveProfile();
        if (profile.Macros.Count + imported.Count > 64)
        {
            StatusText = AppStrings.Text("MacroCountLimit");
            return;
        }

        MacroDocumentService.PrepareImported(
            imported,
            () => Guid.NewGuid().ToString("N"),
            AppStrings.Text("MacroImportedName"));

        var importedRows = new List<MacroRowViewModel>(imported.Count);
        foreach (var macro in imported)
        {
            profile.Macros.Add(macro);
            var row = CreateMacroRow(macro);
            Items.Add(row);
            importedRows.Add(row);
            MarkMacroDirtyId(macro.Id);
        }

        var importedIds = imported.Select(macro => macro.Id).ToArray();
        if (imported.Any(macro => !ValidateMacroTree(macro)))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            RollbackImportedMacros(profile, importedIds, importedRows);
            return;
        }

        if (imported.Any(macro => !ValidateMacroReferences(macro)))
        {
            StatusText = AppStrings.Text("MacroReferenceInvalid");
            RollbackImportedMacros(profile, importedIds, importedRows);
            return;
        }

        foreach (var macro in imported)
        {
            if (macro.Trigger is { } trigger &&
                FindTriggerConflict(macro, trigger) is { } conflict)
            {
                StatusText = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
                RollbackImportedMacros(profile, importedIds, importedRows);
                return;
            }
        }

        // Imported macros were validated above. An unrelated unfinished draft
        // must not roll back the whole import.
        if (!await TrySaveMacroProfileAsync(cancellationToken, prepareDirtyMacros: false))
        {
            RollbackImportedMacros(profile, importedIds, importedRows);
            return;
        }

        var firstRow = importedRows.FirstOrDefault();
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        if (firstRow is not null)
        {
            SelectedMacro = firstRow;
        }
        RefreshMacroRuntime();
        StatusText = imported.Count == 1
            ? AppStrings.Text("MacroImported")
            : AppStrings.FormatText("MacroBundleImported", imported.Count);
    }

    private void RollbackImportedMacros(
        ProfileDefinition profile,
        IReadOnlyList<string> macroIds,
        IReadOnlyList<MacroRowViewModel> rows)
    {
        var ids = macroIds.ToHashSet(StringComparer.Ordinal);
        profile.Macros.RemoveAll(macro => ids.Contains(macro.Id));
        foreach (var row in rows)
        {
            Items.Remove(row);
        }

        if (SelectedMacro is { } selected && rows.Contains(selected))
        {
            SelectedMacro = Items.FirstOrDefault();
        }

        foreach (var id in ids)
        {
            _dirtyMacroIds.Remove(id);
        }

        OnPropertyChanged(nameof(HasUnsavedMacroChanges));
        OnPropertyChanged(nameof(MacroUnsavedVisibility));
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
    }

    private async Task<bool> TrySaveMacroProfileAsync(
        CancellationToken cancellationToken,
        bool prepareDirtyMacros = true)
    {
        if (prepareDirtyMacros)
        {
            var dirtyRows = Items
                .Where(row => _dirtyMacroIds.Contains(row.Id))
                .ToList();
            if (!PrepareMacrosForSave(dirtyRows))
            {
                return false;
            }
        }

        // Only clear the dirty flags that existed when this save started. A
        // field edit made while the file is being written must stay dirty.
        var dirtyAtStart = _dirtyMacroIds.ToDictionary(
            id => id,
            GetMacroEditVersion,
            StringComparer.Ordinal);
        try
        {
            await _macroSaveGate.WaitAsync(cancellationToken);
            try
            {
                if (!await _saveProfileAsync(cancellationToken))
                {
                    StatusText = AppStrings.Text("MacroSaveFailed");
                    return false;
                }
            }
            finally
            {
                _macroSaveGate.Release();
            }

            MarkMacroChangesSaved(dirtyAtStart);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            StatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
            return false;
        }
    }

    private bool PrepareMacrosForSave(IReadOnlyList<MacroRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (!ValidateMacroTree(row.Definition))
            {
                StatusText = AppStrings.Text("MacroStepIncomplete");
                return false;
            }

            if (!ValidateMacroReferences(row.Definition))
            {
                StatusText = AppStrings.Text("MacroReferenceInvalid");
                return false;
            }

            if (MacroValidator.HasUnsupportedHoldPlayback(row.Definition))
            {
                StatusText = AppStrings.Text("MacroHoldRequiresSinglePlayback");
                return false;
            }

            row.Definition.ApplySafeDefaults();
            EnsureMacroName(row);
            row.Refresh();
            if (FindTriggerConflict(row) is { } conflict)
            {
                StatusText = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
                return false;
            }
        }

        return true;
    }

    private bool ValidateMacroTree(MacroDefinition macro) =>
        MacroValidator.ValidateTree(macro);

    private bool ValidateMacroReferences(MacroDefinition macro) =>
        MacroValidator.ValidateReferences(macro, ActiveMacroMap());

    private IReadOnlyDictionary<string, MacroDefinition> ActiveMacroMap() =>
        MacroDocumentService.IndexById(_getActiveProfile().Macros);

    private void EnsureMacroName(MacroRowViewModel row)
    {
        if (!string.IsNullOrWhiteSpace(row.Definition.Name))
        {
            row.Definition.Name = row.Definition.Name.Trim();
            return;
        }

        row.Definition.Name = AppStrings.FormatText(
            "MacroDefaultName",
            Math.Max(1, Items.IndexOf(row) + 1));
    }

    public void MarkMacroDirty() => MarkMacroDirty(SelectedMacro);

    public void MarkMacroDirty(MacroRowViewModel? row)
    {
        if (row is not null)
        {
            MarkMacroDirtyId(row.Id);
        }
    }

    private void RestoreMacroDirtyState(string macroId, bool wasDirty)
    {
        if (wasDirty)
        {
            MarkMacroDirtyId(macroId);
            return;
        }

        if (_dirtyMacroIds.Remove(macroId))
        {
            OnPropertyChanged(nameof(HasUnsavedMacroChanges));
            OnPropertyChanged(nameof(MacroUnsavedVisibility));
        }
    }

    private long GetMacroEditVersion(string macroId) =>
        _macroEditVersions.GetValueOrDefault(macroId);

    private void MarkMacroDirtyId(string macroId)
    {
        // Bump on every edit, including edits made while a save is in flight.
        _macroEditVersions[macroId] = unchecked(GetMacroEditVersion(macroId) + 1);
        if (!_dirtyMacroIds.Add(macroId))
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedMacroChanges));
        OnPropertyChanged(nameof(MacroUnsavedVisibility));
    }

    private void MarkMacroChangesSaved(IReadOnlyDictionary<string, long> savedVersions)
    {
        var changed = false;
        foreach (var (id, savedVersion) in savedVersions)
        {
            if (GetMacroEditVersion(id) == savedVersion)
            {
                changed |= _dirtyMacroIds.Remove(id);
            }
        }

        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedMacroChanges));
        OnPropertyChanged(nameof(MacroUnsavedVisibility));
    }
}
