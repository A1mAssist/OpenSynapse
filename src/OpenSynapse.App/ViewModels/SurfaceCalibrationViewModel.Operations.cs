using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class SurfaceCalibrationViewModel
{
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_readAttempted || _disposed) return;
        await _operation.ConfigureAwait(true);
        if (!_readAttempted && !_disposed && !cancellationToken.IsCancellationRequested)
            await InitializeAsync(cancellationToken).ConfigureAwait(true);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead) return _operation;
        return RunAsync(async token =>
        {
            _status = AppStrings.Text("SurfaceCalibrationReading");
            Changed();
            try
            {
                using var operation = await RazerSurfaceCalibrationOperations.AcquireAsync(_identity, token).ConfigureAwait(true);
                await LoadSavedAsync().ConfigureAwait(true);
                var actual = await _client.ReadAsync(token).ConfigureAwait(true);
                var configuration = actual.Configuration;
                _selectedSurfaceId = null;
                try
                {
                    if (_savedRecord is { } saved && RazerSurfaceCalibrationReadbackPolicy.RetainsSavedConfiguration(
                        _client.CompareReadback(saved.Configuration, actual), saved.Configuration, actual))
                    {
                        configuration = saved.Configuration;
                        _selectedSurfaceId = saved.SelectedSurfaceId;
                    }
                }
                catch (Exception) { _storeError = AppStrings.Text("SurfaceCalibrationStoreFailed"); }
                SetDraft(configuration);
                _status = AppStrings.Text(configuration.Mode == RazerSurfaceCalibrationMode.Unknown
                    ? "SurfaceCalibrationUnknownState" : "SurfaceCalibrationReadComplete");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                SetDraft(new());
                _log?.Invoke($"surface-calibration pid={_identity.ProductId:X4} read failed: {exception}");
                _status = AppStrings.Text("SurfaceCalibrationReadFailed");
            }
            _readAttempted = true;
        }, cancellationToken);
    }

    public void UpdateAvailability(bool isConnected, bool canWrite)
    {
        if (_disposed || _isConnected == isConnected && _canWrite == canWrite) return;
        var disconnected = _isConnected && !isConnected;
        _isConnected = isConnected;
        _canWrite = canWrite;
        Changed();
        if (disconnected) _ = DisconnectAsync();
    }

    private async Task DisconnectAsync()
    {
        try
        {
            if (_manualCancellation is { } cancellation) await cancellation.CancelAsync().ConfigureAwait(true);
            await _session.NotifyDisconnectedAsync().ConfigureAwait(true);
        }
        catch (Exception) { _status = AppStrings.Text("SurfaceCalibrationCleanupFailed"); }
        Changed();
    }

    [NotProfileScoped]
    public Task ApplyAsync(CancellationToken cancellationToken = default) => !CanApply ? Task.CompletedTask :
        RunAsync(token => ApplyAndSaveAsync(DraftConfiguration(), _selectedSurfaceId, token), cancellationToken);

    [NotProfileScoped]
    public Task RestoreSavedAsync(CancellationToken cancellationToken = default) => !CanRestoreSaved ? Task.CompletedTask :
        RunAsync(token => ApplyAndSaveAsync(_savedRecord!.Configuration, _savedRecord.SelectedSurfaceId, token), cancellationToken);

    [NotProfileScoped]
    public Task RestoreDefaultsAsync(CancellationToken cancellationToken = default) => !CanRestoreDefaults ? Task.CompletedTask :
        RunAsync(token => ApplyAndSaveAsync(_defaults!, null, token), cancellationToken);

    private async Task ApplyAndSaveAsync(RazerSurfaceCalibrationConfiguration candidate, string? surfaceId, CancellationToken token)
    {
        var operation = await RazerSurfaceCalibrationOperations.AcquireAsync(_identity, token).ConfigureAwait(true);
        try { await ApplyAndSaveCoreAsync(candidate, surfaceId, token).ConfigureAwait(true); }
        finally
        {
            // An older disk record must not overwrite the applied candidate while saving is pending.
            if (_pendingSave is not null) _pendingSaveLease = operation;
            else operation.Dispose();
        }
    }

    private async Task ApplyAndSaveCoreAsync(RazerSurfaceCalibrationConfiguration candidate, string? surfaceId, CancellationToken token)
    {
        _status = AppStrings.Text("SurfaceCalibrationApplying");
        Changed();
        var original = await _client.ReadAsync(token).ConfigureAwait(true);
        // Validate the complete candidate before a failure can enter the rollback write path.
        _ = _client.CompareReadback(candidate, original);
        try { RazerSurfaceCalibrationStore.RequireRestorableSnapshot(_client.Profile, original); }
        catch (InvalidOperationException)
        {
            _status = AppStrings.Text("SurfaceCalibrationRecoveryUnavailable");
            return;
        }
        try { await _client.ApplyAsync(candidate, token).ConfigureAwait(true); }
        catch (Exception)
        {
            _status = AppStrings.Text("SurfaceCalibrationApplyFailed");
            await RestoreOriginalAsync(original).ConfigureAwait(true);
            return;
        }
        var readback = RazerSurfaceCalibrationReadback.Unavailable;
        RazerSurfaceCalibrationState? appliedState = null;
        try
        {
            var actual = await _client.ReadAsync(token).ConfigureAwait(true);
            appliedState = actual;
            var compared = _client.CompareReadback(candidate, actual);
            if (compared == RazerSurfaceCalibrationReadback.Mismatch)
                _log?.Invoke($"surface-calibration pid={_identity.ProductId:X4} apply readback mismatch " +
                    RazerSurfaceCalibrationReadbackPolicy.DescribeReadback(candidate, actual));
            readback = RazerSurfaceCalibrationReadbackPolicy.NormalizeSampledReadback(compared, candidate, actual);
            if (readback == RazerSurfaceCalibrationReadback.Mismatch)
            {
                _status = AppStrings.Text("SurfaceCalibrationMismatch");
                await RestoreOriginalAsync(original).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception)
        {
            // Once the write succeeds, a failed read or cancellation must not discard its recovery record.
        }
        SetDraft(candidate);
        _selectedSurfaceId = surfaceId;
        var surfaces = _savedRecord?.CustomSurfaces ?? [];
        _pendingSave = new(_identity, _client.Profile.Kind, candidate, DateTimeOffset.UtcNow)
        {
            SelectedSurfaceId = surfaceId,
            AppliedState = appliedState,
            CustomSurfaces = surfaces.Select(surface => surface.Id == surfaceId
                ? surface with { Configuration = candidate } : surface).ToArray(),
        };
        _pendingReadback = readback;
        await SavePendingAsync().ConfigureAwait(true);
    }

    private async Task RestoreOriginalAsync(RazerSurfaceCalibrationState original)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _client.RestoreAsync(original, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            _status += " " + AppStrings.Text("SurfaceCalibrationCleanupFailed");
        }
    }

    private RazerSurfaceCalibrationReadback _pendingReadback;
    private IDisposable? _pendingSaveLease;

    public Task RetrySaveAsync(CancellationToken cancellationToken = default) => !CanRetrySave ? Task.CompletedTask :
        RunAsync(async token =>
        {
            if (_pendingSave is not null)
            {
                await SavePendingAsync().ConfigureAwait(true);
                if (_pendingSave is null)
                {
                    _pendingSaveLease?.Dispose();
                    _pendingSaveLease = null;
                }
            }
            else
            {
                await _session.RetrySaveAsync(token).ConfigureAwait(true);
                RefreshSession(_session);
            }
        }, cancellationToken);

    private async Task SavePendingAsync()
    {
        if (_pendingSave is not { } pending) return;
        try
        {
            await Task.Run(() => _store.Save(pending)).ConfigureAwait(true);
            _pendingSave = null;
            await LoadSavedAsync().ConfigureAwait(true);
            _status = AppliedMessage(_pendingReadback);
        }
        catch (Exception)
        {
            _status = AppStrings.Text("SurfaceCalibrationAppliedUnsaved");
        }
    }

    private async Task LoadSavedAsync()
    {
        try
        {
            var record = await Task.Run(() => _store.Load(_identity)).ConfigureAwait(true);
            if (record is not null && record.Engine != _client.Profile.Kind)
                throw new InvalidDataException("Saved calibration engine differs from this device.");
            _savedRecord = record;
            _storeError = string.Empty;
            UpdateSavedSurfaces();
        }
        catch (Exception)
        {
            _storeError = AppStrings.Text("SurfaceCalibrationStoreFailed");
        }
    }

    private void UpdateSavedSurfaces()
    {
        SavedSurfaces.Clear();
        foreach (var surface in _savedRecord?.CustomSurfaces ?? [])
            SavedSurfaces.Add(new(surface.Id, surface.Name, surface.Configuration));
        Changed();
    }

    private Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (_disposed || _isBusy) return _operation;
        _isBusy = true;
        Changed();
        return _operation = RunCoreAsync(action, cancellationToken);
    }

    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try { await action(linked.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { _status = AppStrings.Text("SurfaceCalibrationSessionCancelled"); }
        catch (Exception exception)
        {
            _log?.Invoke($"surface-calibration pid={_identity.ProductId:X4} operation failed: {exception}");
            _status = AppStrings.Text(exception is ArgumentException ? "SurfaceCalibrationInvalidValues" : "SurfaceCalibrationApplyFailed");
        }
        finally { _isBusy = false; Changed(); }
    }

    private static string AppliedMessage(RazerSurfaceCalibrationReadback readback) => AppStrings.Text(
        readback == RazerSurfaceCalibrationReadback.Consistent ? "SurfaceCalibrationApplied" : "SurfaceCalibrationReadbackUnavailable");
}
