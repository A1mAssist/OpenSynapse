using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    private sealed record BladeBrightnessRequest(
        BladeLightingProfileEdit Edit, byte Value, IReadOnlyList<DeviceDescriptor> Devices, long Generation);

    private BladeBrightnessRequest CaptureBrightness(byte value, PowerSourceKind power) => new(
        new BladeLightingProfileEdit(Profile, power), value, ActiveDescriptors.ToArray(),
        Interlocked.Increment(ref _bladeBrightnessVerificationGeneration));

    private async Task ApplyBrightnessCoreAsync(CancellationToken cancellationToken)
    {
        var request = CaptureBrightness(ToBladeBrightness(BrightnessPercent), SelectedLightingPowerState);
        var actual = request.Value;
        if (request.Edit.CanWriteHardware(Profile, CurrentPowerSource, IsDisplayAvailable))
        {
            actual = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                request.Devices, actual, cancellationToken);
            SetBrightness(actual);
        }
        await SaveBrightnessRequestAsync(request, actual, cancellationToken);
        RefreshLightingEditor();
    }

    internal Task QueueBrightnessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness) return Task.CompletedTask;
        EnqueueBrightness(CaptureBrightness(ToBladeBrightness(BrightnessPercent), SelectedLightingPowerState));
        return Task.CompletedTask;
    }

    internal Task StepBrightnessAsync(bool increase, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness)
            throw new InvalidOperationException(AppStrings.Text("Text_F00EAF88"));
        // Fn changes the actual power profile, not the power tab currently being edited.
        var edit = new BladeLightingProfileEdit(Profile, CurrentPowerSource);
        lock (_bladeBrightnessGate)
        {
            var pending = _bladeBrightnessRequests.LastOrDefault(item => ReferenceEquals(item.Edit.Blade, edit.Blade))
                ?? (_writingBladeBrightness is { } writing && ReferenceEquals(writing.Edit.Blade, edit.Blade)
                    ? writing : null);
            var current = pending is not null ? pending.Value :
                edit.Blade.KeyboardBrightness ?? ToBladeBrightness(_blade._confirmedBladeBrightnessPercent);
            var requested = (byte)Math.Clamp(current + (increase ? 16 : -16), 0, 255);
            if (requested != current)
                EnqueueBrightness(CaptureBrightness(requested, CurrentPowerSource));
        }
        return Task.CompletedTask;
    }

    private void EnqueueBrightness(BladeBrightnessRequest request)
    {
        lock (_bladeBrightnessGate)
        {
            _bladeBrightnessRequests.RemoveAll(item => ReferenceEquals(item.Edit.Blade, request.Edit.Blade));
            if (ReferenceEquals(request.Edit.Blade, EditableLightingBladeProfile))
                SetBrightness(request.Value, confirm: false);
            _bladeBrightnessRequests.Add(request);
            if (!_bladeBrightnessWriterActive)
            {
                _bladeBrightnessWriterActive = true;
                _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
            }
        }
    }

    private Task SaveBrightnessRequestAsync(BladeBrightnessRequest request, byte value, CancellationToken ct) =>
        request.Edit.SaveBrightnessAsync(Profile, value, SaveProfileAsync, ct, AppStrings.Text("Text_C7D0CD75"));

    private async Task WriteDesiredBladeBrightnessAsync()
    {
        try { await WriteDesiredBladeBrightnessCoreAsync(); }
        finally
        {
            lock (_bladeBrightnessGate)
            {
                _bladeBrightnessWriterActive = false;
                if (_bladeBrightnessRequests.Count > 0 && !IsDisposed)
                {
                    _bladeBrightnessWriterActive = true;
                    _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
                }
            }
        }
    }

    private async Task WriteDesiredBladeBrightnessCoreAsync()
    {
        while (!IsDisposed)
        {
            BladeBrightnessRequest request;
            lock (_bladeBrightnessGate)
            {
                if (_bladeBrightnessRequests.Count == 0) return;
                request = _bladeBrightnessRequests[0];
                _bladeBrightnessRequests.RemoveAt(0);
                _writingBladeBrightness = request;
            }
            var applied = false;
            await RunDeviceOperationAsync(AppStrings.Text("Text_5F0C27DB"), async () =>
            {
                var writesHardware = request.Edit.CanWriteHardware(Profile, CurrentPowerSource, IsDisplayAvailable);
                var original = ToBladeBrightness(_blade._confirmedBladeBrightnessPercent);
                if (writesHardware)
                {
                    try
                    {
                        await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                            request.Devices, request.Value, CancellationToken.None, verifyReadback: false);
                    }
                    catch (Exception writeError) when (RuntimeErrors.IsExpected(writeError))
                    {
                        try
                        {
                            await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                                request.Devices, original, CancellationToken.None);
                        }
                        catch (Exception restoreError) when (RuntimeErrors.IsExpected(restoreError))
                        {
                            throw new AggregateException(writeError, restoreError);
                        }
                        throw;
                    }
                }
                // Persist to the captured target even if power/tab/profile selection changed.
                await SaveBrightnessRequestAsync(request, request.Value, CancellationToken.None);
                if (writesHardware) SetBrightness(request.Value);
                RefreshLightingEditor();
                applied = true;
                if (writesHardware && !IsDisposed) ScheduleBladeBrightnessVerification(request);
            }, CancellationToken.None, RefreshLightingEditor, successVerb: AppStrings.Text("Text_D25102CE"));
            lock (_bladeBrightnessGate) _writingBladeBrightness = null;
            if (!applied) RequestDeviceRefresh();
        }
    }

    private void ScheduleBladeBrightnessVerification(BladeBrightnessRequest request) =>
        _bladeBrightnessVerification = VerifyBladeBrightnessAfterIdleAsync(request);

    private bool CanVerifyBrightness(BladeBrightnessRequest request) =>
        request.Generation == Volatile.Read(ref _bladeBrightnessVerificationGeneration) && !IsDisposed &&
        request.Edit.CanWriteHardware(Profile, CurrentPowerSource, IsDisplayAvailable);

    private async Task VerifyBladeBrightnessAfterIdleAsync(BladeBrightnessRequest request)
    {
        await Task.Delay(BrightnessVerificationDelay);
        if (!CanVerifyBrightness(request)) return;
        await RunDeviceOperationAsync(AppStrings.Text("Text_5F0C27DB"), async () =>
        {
            if (!CanVerifyBrightness(request)) return;
            var actual = await _deviceTelemetryReader.ReadBladeKeyboardBrightnessAsync(
                request.Devices, CancellationToken.None);
            if (!CanVerifyBrightness(request)) return;
            await SaveBrightnessRequestAsync(request, actual, CancellationToken.None);
            SetBrightness(actual);
            RefreshLightingEditor();
        }, CancellationToken.None, RequestDeviceRefresh);
    }
}
