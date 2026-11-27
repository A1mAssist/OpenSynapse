using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class SurfaceCalibrationViewModel
{
    private CancellationTokenSource? _manualCancellation;
    private bool _preparingSession;
    private RazerSurfaceCalibrationSession CreateSession()
    {
        var session = new RazerSurfaceCalibrationSession(_client, _identity, _input, _store);
        var previousState = session.State;
        session.StateChanged += state =>
        {
            if (state != previousState)
            {
                previousState = state;
                _log?.Invoke($"surface-calibration pid={_identity.ProductId:X4} session={session.SessionId} state={state} error={session.Error} cleanup={session.CleanupError} readback={session.ReadbackDetail}");
            }
            SessionStateChanged(session);
        };
        return session;
    }

    public Task PrepareAsync(CancellationToken cancellationToken = default) =>
        !CanPrepare ? Task.CompletedTask : PrepareSessionAsync(false, cancellationToken);

    public Task PrepareMiniAsync(CancellationToken cancellationToken = default) =>
        !CanPrepareMini ? Task.CompletedTask : PrepareSessionAsync(true, cancellationToken);

    private Task PrepareSessionAsync(bool mini, CancellationToken cancellationToken)
    {
        var previousCancellation = _manualCancellation;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _manualCancellation = cancellation;
        _preparingSession = true;
        return RunAsync(async token =>
        {
            try
            {
                await _session.DisposeAsync().ConfigureAwait(true);
                previousCancellation?.Dispose();
                cancellation.Token.ThrowIfCancellationRequested();
                _session = CreateSession();
                _session.SurfaceName = string.IsNullOrWhiteSpace(SurfaceName)
                    ? AppStrings.FormatText("SurfaceCalibrationNewSurfaceName", SavedSurfaces.Count + 1) : SurfaceName.Trim();
                _draft = _draft with { Mode = mini ? RazerSurfaceCalibrationMode.Mini : RazerSurfaceCalibrationMode.Manual, RawData = null };
                _selectedSurfaceId = null;
                FillDraftRanges();
                if (!IsValidDraft(requireRaw: false))
                {
                    _status = AppStrings.Text("SurfaceCalibrationInvalidValues");
                    return;
                }
                await _session.PrepareAsync(DraftConfiguration(), cancellation.Token).ConfigureAwait(true);
                RefreshSession(_session);
            }
            finally { _preparingSession = false; Changed(); }
        }, cancellation.Token);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => !CanStart ? Task.CompletedTask :
        RunAsync(async token =>
        {
            await _session.StartAsync(token).ConfigureAwait(true);
            RefreshSession(_session);
        }, cancellationToken);

    public Task FinishAsync(CancellationToken cancellationToken = default) => !CanFinish ? Task.CompletedTask :
        RunAsync(async token =>
        {
            await _session.FinishAsync(token).ConfigureAwait(true);
            RefreshSession(_session);
        }, cancellationToken);

    public async Task CancelAsync()
    {
        if (_disposed || !IsSessionActive) return;
        try
        {
            if (_manualCancellation is { } cancellation) await cancellation.CancelAsync().ConfigureAwait(true);
            await _operation.ConfigureAwait(true);
            await _session.CancelAsync().ConfigureAwait(true);
            RefreshSession(_session);
        }
        catch (Exception) { _status = AppStrings.Text("SurfaceCalibrationCleanupFailed"); Changed(); }
    }

    private void SessionStateChanged(RazerSurfaceCalibrationSession session)
    {
        if (_disposed || !ReferenceEquals(session, _session)) return;
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
            _dispatcher.TryEnqueue(() => RefreshSession(session));
        else RefreshSession(session);
    }

    private void RefreshSession(RazerSurfaceCalibrationSession session)
    {
        if (_disposed || !ReferenceEquals(session, _session)) return;
        _status = session.State switch
        {
            RazerSurfaceCalibrationSessionState.Completed => AppliedMessage(session.Readback),
            RazerSurfaceCalibrationSessionState.AppliedUnsaved => AppStrings.Text("SurfaceCalibrationAppliedUnsaved"),
            RazerSurfaceCalibrationSessionState.Failed when session.Readback == RazerSurfaceCalibrationReadback.Mismatch => AppStrings.Text("SurfaceCalibrationMismatch"),
            RazerSurfaceCalibrationSessionState.Failed when session.Error is NotSupportedException => AppStrings.Text("SurfaceCalibrationInputUnavailable"),
            _ => AppStrings.Text($"SurfaceCalibrationSession{session.State}"),
        };
        if (session.State is RazerSurfaceCalibrationSessionState.Completed or RazerSurfaceCalibrationSessionState.AppliedUnsaved)
        {
            if (session.Candidate is { } candidate) SetDraft(candidate);
            if (session.State == RazerSurfaceCalibrationSessionState.Completed && session.SavedRecord is { } record)
            {
                _savedRecord = record;
                _storeError = string.Empty;
                _selectedSurfaceId = record.SelectedSurfaceId;
                UpdateSavedSurfaces();
            }
        }
        if (session.CleanupError is not null)
            _status += " " + AppStrings.Text("SurfaceCalibrationCleanupFailed");
        Changed();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        RazerSurfaceCalibrationOperations.AvailabilityChanged -= OperationsAvailabilityChanged;
        await _lifetime.CancelAsync().ConfigureAwait(true);
        await _session.DisposeAsync().ConfigureAwait(true);
        await _operation.ConfigureAwait(true);
        _pendingSaveLease?.Dispose();
        _pendingSaveLease = null;
        _manualCancellation?.Dispose();
        _lifetime.Dispose();
    }
}
