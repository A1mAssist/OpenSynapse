using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    internal async Task<SoftwareBindingRestoreTransaction?> BeginSoftwareBindingRestoreAsync(
        IReadOnlyList<MacroSoftwareBinding> bindings, CancellationToken cancellationToken)
    {
        if (_deviceService is null) return null;
        var profile = _getActiveProfile();
        var undo = new List<Func<Task>>();
        async Task RollbackAsync()
        {
            var errors = new List<Exception>();
            foreach (var rollback in undo.AsEnumerable().Reverse())
            {
                try { await rollback().ConfigureAwait(true); }
                catch (Exception exception) { errors.Add(exception); }
            }
            if (errors.Count > 0) throw new AggregateException("Keypad binding restore rollback failed.", errors);
        }

        try
        {
            foreach (var binding in bindings)
            {
                if (!RazerKeypadInputCatalog.UsesHostMapping(binding.ProductId))
                    throw new ArgumentException("Only keypad bindings belong to this restore transaction.", nameof(bindings));
                var matches = CurrentConnections.Where(candidate => candidate.IsReady &&
                    candidate.Definition.ProductId == binding.ProductId &&
                    (string.IsNullOrWhiteSpace(binding.InstanceId) ||
                     StringComparer.OrdinalIgnoreCase.Equals(candidate.InstanceId, binding.InstanceId))).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException("The keypad binding does not identify one connected device.");
                var connection = matches[0];
                var previousAssignment = await _deviceService.GetButtonAssignmentAsync(
                    connection, binding.ButtonId, binding.Layer, cancellationToken).ConfigureAwait(true);
                var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
                if (!profile.Devices.TryGetValue(key, out var settings))
                    profile.Devices[key] = settings = new DeviceProfileSettings();
                var previousMappings = settings.Keyboard.ButtonMappings;
                var mappings = (previousMappings ?? ProfileResolver.Resolve(
                    _getProfile(), connection.ToDescriptor(), _currentPowerSource()).Keyboard.ButtonMappings ?? [])
                    .Where(item => item.ButtonId != binding.ButtonId || (item.Mode ?? 0) != binding.Layer)
                    .Select(item => item.Clone()).ToList();
                mappings.Add(new ButtonMappingProfile
                {
                    ButtonId = binding.ButtonId, Mode = binding.Layer,
                    FunctionId = binding.OriginalFunctionId, FunctionData = binding.OriginalData.ToList(),
                });
                undo.Add(async () =>
                {
                    settings.Keyboard.ButtonMappings = previousMappings;
                    await _deviceService.SetButtonAssignmentAsync(connection, binding.ButtonId, binding.Layer,
                        previousAssignment.FunctionId, previousAssignment.Data, CancellationToken.None).ConfigureAwait(true);
                });
                await _deviceService.SetButtonAssignmentAsync(connection, binding.ButtonId, binding.Layer,
                    binding.OriginalFunctionId, binding.OriginalData, cancellationToken).ConfigureAwait(true);
                settings.Keyboard.ButtonMappings = mappings;
            }
            return new SoftwareBindingRestoreTransaction(RollbackAsync);
        }
        catch (Exception exception)
        {
            await RollbackAsync().ConfigureAwait(true);
            if (exception is OperationCanceledException) throw;
            _diagnosticLog.TryWrite("mapping", $"keypad binding restore failed: {exception.Message}");
            return null;
        }
    }

    internal async Task<bool> RestoreSoftwareMacroBindingAsync(
        MacroSoftwareBinding binding,
        CancellationToken cancellationToken)
    {
        if (RazerKeypadInputCatalog.UsesHostMapping(binding.ProductId))
        {
            await using var transaction = await BeginSoftwareBindingRestoreAsync([binding], cancellationToken);
            if (transaction is null) return false;
            transaction.Commit();
            return true;
        }
        if (_deviceService is null)
        {
            _diagnosticLog.TryWrite(
                "mapping",
                $"software binding restore skipped: no device service (pid=0x{binding.ProductId:X4})");
            return false;
        }

        var candidates = CurrentConnections.Where(candidate =>
            candidate.Definition.ProductId == binding.ProductId &&
            (string.IsNullOrWhiteSpace(binding.InstanceId) ||
             StringComparer.OrdinalIgnoreCase.Equals(candidate.InstanceId, binding.InstanceId))).ToArray();
        var connection = candidates.Length == 1 ? candidates[0] : null;
        if (connection is null)
        {
            _diagnosticLog.TryWrite(
                "mapping",
                $"software binding restore skipped: pid=0x{binding.ProductId:X4} is offline");
            return false;
        }

        try
        {
            await _deviceService.SetButtonAssignmentAsync(
                connection,
                binding.ButtonId,
                binding.Layer,
                binding.OriginalFunctionId,
                binding.OriginalData,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnosticLog.TryWrite(
                "mapping",
                $"software binding restore failed: {exception.Message}");
            return false;
        }
    }
}
