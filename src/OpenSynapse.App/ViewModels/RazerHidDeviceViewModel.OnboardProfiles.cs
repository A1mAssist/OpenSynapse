using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private byte _onboardProfileCount;
    private byte _onboardMaxProfiles;
    private byte _activeOnboardProfileId;
    private IReadOnlyList<byte> _onboardProfileIds = [];
    private readonly Dictionary<byte, string> _onboardProfileNames = [];
    private string _onboardProfileNameSignature = string.Empty;
    private int _selectedOnboardProfileIndex = -1;
    private bool _activeOnboardProfileKnown;
    private string _onboardProfileStatusText = string.Empty;
    private bool _onboardProfileStatusIsError;

    /// <summary>
    /// The class 0x03 profile dialect (DeathAdder V4 Pro) has no reset command, so the
    /// button would always fail with a device rejection.
    /// </summary>
    public Visibility OnboardProfileResetVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.OnboardProfileWrite) &&
            RazerHidDeviceService.SupportsOnboardProfileReset(Connection.Definition.ProductId));
    public Visibility OnboardProfileVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.OnboardProfileRead, RazerHidBackendCapability.OnboardProfileWrite));
    public Visibility OnboardProfileWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.OnboardProfileWrite));
    public Visibility OnboardProfileDeleteBlockedVisibility =>
        VisibleWhen(IsOnboardProfileActiveSelected && _onboardProfileIds.Count > 1);
    public byte OnboardProfileCount
    {
        get => _onboardProfileCount;
        set => SetOnboardProfileField(ref _onboardProfileCount, value, nameof(OnboardProfileCount),
            nameof(CanCreateOnboardProfile), nameof(CanDeleteOnboardProfile));
    }
    public byte OnboardMaxProfiles
    {
        get => _onboardMaxProfiles;
        set => SetOnboardProfileField(ref _onboardMaxProfiles, value, nameof(OnboardMaxProfiles),
            nameof(OnboardProfileMaximumId), nameof(CanCreateOnboardProfile),
            nameof(CanDeleteOnboardProfile));
    }
    public byte ActiveOnboardProfileId
    {
        get => _activeOnboardProfileId;
        set => SetOnboardProfileField(ref _activeOnboardProfileId, value, nameof(ActiveOnboardProfileId),
            nameof(ActiveOnboardProfileText), nameof(CanApplyOnboardProfile),
            nameof(IsOnboardProfileActiveSelected), nameof(CanDeleteOnboardProfile),
            nameof(OnboardProfileDeleteBlockedVisibility));
    }
    /// <summary>
    /// The device's exact profile ids. Empty until the first read; the fallback range is
    /// built from count/max when 0x81 is unavailable.
    /// </summary>
    public IReadOnlyList<string> OnboardProfileOptions =>
        _onboardProfileIds.Select(OnboardProfileLabel).ToArray();

    private string OnboardProfileLabel(byte id) =>
        _onboardProfileNames.TryGetValue(id, out var name)
            ? $"{name} ({id.ToString(CultureInfo.InvariantCulture)})"
            : id.ToString(CultureInfo.InvariantCulture);
    public int SelectedOnboardProfileIndex
    {
        get => _selectedOnboardProfileIndex;
        set
        {
            // -1 is the ComboBox's own reset while ItemsSource is replaced; clamping
            // it to 0 is what made the selector drift back to profile 1.
            var normalized = value < 0 || _onboardProfileIds.Count == 0
                ? -1
                : Math.Min(value, _onboardProfileIds.Count - 1);
            if (!SetField(ref _selectedOnboardProfileIndex, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedOnboardProfileId));
            OnPropertyChanged(nameof(CanApplyOnboardProfile));
            OnPropertyChanged(nameof(CanDeleteOnboardProfile));
            OnPropertyChanged(nameof(IsOnboardProfileActiveSelected));
            OnPropertyChanged(nameof(OnboardProfileDeleteBlockedVisibility));
        }
    }
    public byte SelectedOnboardProfileId =>
        _selectedOnboardProfileIndex >= 0 && _selectedOnboardProfileIndex < _onboardProfileIds.Count
            ? _onboardProfileIds[_selectedOnboardProfileIndex]
            : (byte)0;
    public string OnboardProfileStatusText
    {
        get => _onboardProfileStatusText;
        set => SetField(ref _onboardProfileStatusText, value);
    }
    public Brush OnboardProfileStatusBrush => new SolidColorBrush(_onboardProfileStatusIsError
        ? Color.FromArgb(255, 255, 107, 107)
        : Color.FromArgb(255, 160, 160, 160));
    public string ActiveOnboardProfileText => _activeOnboardProfileKnown
        ? OnboardProfileLabel(ActiveOnboardProfileId)
        : AppStrings.Text("RazerHidOnboardProfileUnknown");
    /// <summary>
    /// Profile ids are one-based; a device that reports one profile only knows id 1.
    /// </summary>
    public int OnboardProfileMaximumId => Math.Max(1, (int)OnboardMaxProfiles);
    public bool IsOnboardProfileActiveSelected => _activeOnboardProfileKnown &&
        SelectedOnboardProfileId != 0 && SelectedOnboardProfileId == ActiveOnboardProfileId;
    public bool CanApplyOnboardProfile => CanEditOnboardProfile && _activeOnboardProfileKnown &&
        SelectedOnboardProfileId != 0 && !IsOnboardProfileActiveSelected;
    public bool CanCreateOnboardProfile => CanEditOnboardProfile && FindFreeOnboardProfileId() is not null;
    public bool CanDeleteOnboardProfile => CanEditOnboardProfile && _activeOnboardProfileKnown &&
        _onboardProfileIds.Count > 1 && !IsOnboardProfileActiveSelected;
    public bool CanResetOnboardProfile => CanEditOnboardProfile &&
        RazerHidDeviceService.SupportsOnboardProfileReset(Connection.Definition.ProductId);

    private byte? FindFreeOnboardProfileId()
    {
        for (var id = 1; id <= OnboardProfileMaximumId; id++)
        {
            if (!_onboardProfileIds.Contains((byte)id))
            {
                return (byte)id;
            }
        }

        return null;
    }

    private int IndexOfOnboardProfile(byte profileId)
    {
        for (var index = 0; index < _onboardProfileIds.Count; index++)
        {
            if (_onboardProfileIds[index] == profileId)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The onboard profile card enables its buttons from the profile counters, so a
    /// changed counter has to refresh the dependent commands.
    /// </summary>
    private void SetOnboardProfileField<T>(
        ref T field,
        T value,
        string propertyName,
        params string[] dependentProperties)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        foreach (var property in dependentProperties)
        {
            OnPropertyChanged(property);
        }
    }

    public async Task LoadOnboardProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsOnboardProfileBusy ||
            !CanUse(RazerHidBackendCapability.OnboardProfileRead))
        {
            return;
        }
        IsOnboardProfileBusy = true;
        SetOnboardProfileStatus(string.Empty, isError: false);
        try
        {
            await ReadOnboardProfilesAsync(cancellationToken);
        }
        finally
        {
            IsOnboardProfileBusy = false;
        }
    }

    /// <summary>
    /// Reads each field independently: a rejected active or id-list command must not
    /// blank the entire card. Returns true when the exact id list was read.
    /// </summary>
    private async Task<bool> ReadOnboardProfilesAsync(CancellationToken cancellationToken)
    {
        var currentId = SelectedOnboardProfileId;
        var idsUnavailable = false;
        IReadOnlyList<byte>? ids = null;

        try
        {
            OnboardMaxProfiles = await _service.GetMaxOnboardProfilesAsync(Connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedOnboardProfileReadFailure(exception))
        {
        }

        try
        {
            OnboardProfileCount = await _service.GetOnboardProfileCountAsync(Connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedOnboardProfileReadFailure(exception))
        {
        }

        try
        {
            ids = await _service.GetOnboardProfileIdsAsync(Connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedOnboardProfileReadFailure(exception))
        {
            idsUnavailable = true;
        }

        try
        {
            var active = await _service.GetActiveOnboardProfileIdAsync(Connection, cancellationToken);
            ActiveOnboardProfileId = active;
            _activeOnboardProfileKnown = active != 0 && active <= OnboardMaxProfiles &&
                (ids is null || ids.Contains(active));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedOnboardProfileReadFailure(exception))
        {
            _activeOnboardProfileKnown = false;
        }

        var nameSignature = string.Join(',', ids ?? []) +
            $"|{OnboardProfileCount}|{OnboardMaxProfiles}";
        var namesChanged = !StringComparer.Ordinal.Equals(
            _onboardProfileNameSignature, nameSignature);
        var namesResolved = true;
        if (ids is not null && namesChanged)
        {
            // Read into a scratch table so a timeout keeps the names already on screen.
            var names = new Dictionary<byte, string>();
            // The middleware only reads name structures for profiles after the first
            // slot; slot 1 is the default profile and has no stored name.
            foreach (var id in ids.Skip(1))
            {
                try
                {
                    var name = await _service
                        .GetOnboardProfileNameAsync(Connection, id, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names[id] = name.Trim();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedOnboardProfileReadFailure(exception))
                {
                    // Names are optional; one rejected 0x88 means this firmware will
                    // reject the rest too, so don't delay the card with every profile.
                    // A timeout is different: the table may still answer next refresh.
                    namesResolved = exception is not (RazerDeviceTimeoutException or
                        RazerReportMismatchException);
                    break;
                }
            }
            if (namesResolved)
            {
                _onboardProfileNames.Clear();
            }
            foreach (var pair in names)
            {
                _onboardProfileNames[pair.Key] = pair.Value;
            }
        }
        if (namesChanged && namesResolved)
        {
            _onboardProfileNameSignature = nameSignature;
        }

        _onboardProfileIds = ids ?? BuildFallbackProfileIds(OnboardProfileCount, OnboardMaxProfiles);
        OnPropertyChanged(nameof(OnboardProfileOptions));
        OnPropertyChanged(nameof(ActiveOnboardProfileText));

        if (idsUnavailable && OnboardMaxProfiles > 0)
        {
            SetOnboardProfileStatus(AppStrings.Text("RazerHidOnboardProfileIdsUnavailable"), isError: false);
        }

        var targetId = _activeOnboardProfileKnown ? ActiveOnboardProfileId : currentId;
        var index = IndexOfOnboardProfile(targetId);
        SelectedOnboardProfileIndex = index >= 0 ? index : (_onboardProfileIds.Count > 0 ? 0 : -1);
        NotifyStateChanged();
        return ids is not null;
    }

    /// <summary>
    /// Without 0x81 the best available approximation is ids 1..count. Create still
    /// picks the first id outside this range, so it never reuses the selected slot.
    /// </summary>
    private static IReadOnlyList<byte> BuildFallbackProfileIds(byte count, byte max)
    {
        var limit = Math.Min(count, Math.Max(max, (byte)1));
        var ids = new List<byte>(limit);
        for (var id = 1; id <= limit; id++)
        {
            ids.Add((byte)id);
        }

        return ids;
    }

    private static bool IsExpectedOnboardProfileReadFailure(Exception exception) =>
        exception is RazerCommandRejectedException or NotSupportedException or
            RazerDeviceTimeoutException or RazerReportMismatchException or
            InvalidOperationException or IOException;

    [ProfileSetting("mouse.onboard-profile", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyActiveOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            "switch",
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                if (!_activeOnboardProfileKnown || SelectedOnboardProfileId == 0)
                {
                    return;
                }

                var requested = SelectedOnboardProfileId;
                var previous = ActiveOnboardProfileId;
                var previousProfile = ReadProfile(settings => settings.Viper.ActiveOnboardProfileId);
                await _service.SetActiveOnboardProfileIdAsync(
                    Connection, requested, cancellationToken);
                await ReadOnboardProfilesAsync(cancellationToken);
                if (_activeOnboardProfileKnown && ActiveOnboardProfileId != requested)
                {
                    SetOnboardProfileStatus(
                        AppStrings.Text("RazerHidOnboardProfileNotApplied"), isError: true);
                    return;
                }
                var verified = _activeOnboardProfileKnown;
                await SaveProfileAsync(
                    settings => settings.Viper.ActiveOnboardProfileId = requested,
                    settings => settings.Viper.ActiveOnboardProfileId = previousProfile,
                    () =>
                    {
                        _activeOnboardProfileKnown = true;
                        SelectedOnboardProfileIndex = IndexOfOnboardProfile(previous);
                        ActiveOnboardProfileId = previous;
                    },
                    token => _service.SetActiveOnboardProfileIdAsync(Connection, previous, token),
                    cancellationToken);
                // The readouts above the card still hold the previous slot's DPI,
                // lighting and mapping until they are re-read from the device.
                var readoutsUnverified = false;
                try
                {
                    await LoadBasicStateAsync(cancellationToken);
                    await LoadMappingAsync(cancellationToken);
                }
                catch (Exception exception) when (IsExpectedOnboardProfileReadFailure(exception))
                {
                    readoutsUnverified = true;
                }
                SetOnboardProfileStatus(
                    AppStrings.Text(verified && !readoutsUnverified
                        ? "RazerHidOperationApplied"
                        : "RazerHidOnboardProfileAppliedUnverified"),
                    isError: false);
            }, cancellationToken);

    public Task CreateOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            "create",
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                if (FindFreeOnboardProfileId() is not { } newId)
                {
                    SetOnboardProfileStatus(AppStrings.Text("RazerHidOnboardProfileNoFreeSlot"), isError: false);
                    return;
                }

                var previousCount = OnboardProfileCount;
                await _service.CreateOnboardProfileAsync(Connection, newId, cancellationToken);
                var created = false;
                var readbackAnswered = false;
                for (var attempt = 0; attempt < 4 && !created; attempt++)
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(120, cancellationToken);
                    }

                    var idsRead = await ReadOnboardProfilesAsync(cancellationToken);
                    var countGrew = OnboardProfileCount > previousCount;
                    readbackAnswered |= idsRead || countGrew;
                    created = idsRead ? _onboardProfileIds.Contains(newId) : countGrew;
                }

                if (!created)
                {
                    SetOnboardProfileStatus(
                        AppStrings.Text(readbackAnswered
                            ? "RazerHidOnboardProfileCreateNotApplied"
                            : "RazerHidOnboardProfileAppliedUnverified"),
                        isError: readbackAnswered);
                    return;
                }

                SelectedOnboardProfileIndex = IndexOfOnboardProfile(newId);
                SetOnboardProfileStatus(AppStrings.Text("RazerHidOperationApplied"), isError: false);
            }, cancellationToken);

    public Task DeleteOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            "delete",
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                var deleted = SelectedOnboardProfileId;
                if (!_activeOnboardProfileKnown || deleted == 0 || IsOnboardProfileActiveSelected)
                {
                    return;
                }

                var previousCount = OnboardProfileCount;
                await _service.DeleteOnboardProfileAsync(Connection, deleted, cancellationToken);
                var removed = false;
                var readbackAnswered = false;
                for (var attempt = 0; attempt < 4 && !removed; attempt++)
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(120, cancellationToken);
                    }

                    var idsRead = await ReadOnboardProfilesAsync(cancellationToken);
                    var countShrank = OnboardProfileCount < previousCount;
                    readbackAnswered |= idsRead || countShrank;
                    removed = idsRead ? !_onboardProfileIds.Contains(deleted) : countShrank;
                }

                if (!removed)
                {
                    SetOnboardProfileStatus(
                        AppStrings.Text(readbackAnswered
                            ? "RazerHidOnboardProfileDeleteNotApplied"
                            : "RazerHidOnboardProfileAppliedUnverified"),
                        isError: readbackAnswered);
                    return;
                }

                SetOnboardProfileStatus(AppStrings.Text("RazerHidOperationApplied"), isError: false);
            }, cancellationToken);

    public Task ResetOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            "reset",
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                if (SelectedOnboardProfileId == 0)
                {
                    return;
                }

                try
                {
                    await _service.ResetOnboardProfileAsync(Connection, SelectedOnboardProfileId, cancellationToken);
                }
                catch (NotSupportedException)
                {
                    // 02B9 and similar keyboards reject 05:0B even though the bundle declares it.
                    SetOnboardProfileStatus(
                        AppStrings.Text("RazerHidOnboardProfileResetUnsupported"), isError: true);
                    return;
                }

                var readback = await ReadOnboardProfilesAsync(cancellationToken);
                SetOnboardProfileStatus(
                    AppStrings.Text(readback
                        ? "RazerHidOperationApplied"
                        : "RazerHidOnboardProfileAppliedUnverified"),
                    isError: false);
            }, cancellationToken);

    /// <summary>
    /// A profile command the device refuses is a user-facing answer, not a transport
    /// failure: the card keeps working and the reason is shown next to the buttons.
    /// </summary>
    private async Task RunOnboardProfileWriteAsync(
        string operationName,
        Func<bool> isBusy,
        Action<bool> setBusy,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (!CanUse(RazerHidBackendCapability.OnboardProfileWrite) || isBusy())
        {
            return;
        }

        setBusy(true);
        SetOnboardProfileStatus(string.Empty, isError: false);
        try
        {
            await _service.RunExclusiveAsync(Connection, _ => operation(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Invoke(
                $"onboard-profile pid={Connection.Definition.ProductId:X4} " +
                $"operation={operationName} failed: {exception.GetType().Name}: {exception.Message}");
            SetOnboardProfileError(exception);
        }
        finally
        {
            setBusy(false);
        }
    }

    private void SetOnboardProfileStatus(string text, bool isError)
    {
        OnboardProfileStatusText = text;
        if (_onboardProfileStatusIsError == isError)
        {
            return;
        }

        _onboardProfileStatusIsError = isError;
        OnPropertyChanged(nameof(OnboardProfileStatusBrush));
    }

    private void SetOnboardProfileError(Exception exception)
    {
        var key = exception switch
        {
            RazerCommandRejectedException => "RazerHidOnboardProfileRejected",
            NotSupportedException => "RazerHidOnboardProfileUnsupported",
            RazerDeviceTimeoutException { IsRetryableBusy: true } => "RazerHidOnboardProfileBusy",
            RazerDeviceTimeoutException => "RazerHidOnboardProfileTimeout",
            RazerReportMismatchException => "RazerHidOnboardProfileMismatch",
            InvalidOperationException => "RazerHidOnboardProfileInvalidResponse",
            _ => "RazerHidOnboardProfileFailed",
        };
        SetOnboardProfileStatus(AppStrings.Text(key), isError: true);
    }
}
