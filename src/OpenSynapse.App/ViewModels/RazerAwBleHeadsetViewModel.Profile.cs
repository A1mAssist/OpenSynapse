using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;

    public RazerAwBleHeadsetViewModel(
        RazerBleDevice device,
        Action<string>? diagnostic = null,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null)
        : this(
            device,
            cancellationToken => ConnectAsync(device, diagnostic, cancellationToken),
            getProfile,
            saveProfile)
    {
    }

    internal RazerAwBleHeadsetViewModel(
        RazerBleDevice device,
        Func<CancellationToken, Task<RazerAwBleHeadsetService?>> connect,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        _device = device;
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _getProfile = getProfile;
        _saveProfile = saveProfile;
    }

    private string ProfileDeviceKey => "ble:" + _device.Id;

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_service is null || _getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey,
            Name,
            0x1532,
            0,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            0,
            1,
            2,
            "aw-ble",
            DeviceCategory.Headset);
        var applier = new HeadsetPeripheralProfileApplier(
            (_, _) => Task.FromResult<RazerAwBleHeadsetService?>(_service));
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(
                document,
                descriptor,
                powerSource,
                ProfileResolver.Resolve(document, descriptor, powerSource)),
            cancellationToken);
    }

    private async Task RunWriteAsync<T>(
        Func<RazerAwBleHeadsetService, Task<T>> write,
        Action<T> apply,
        Func<T, CancellationToken, Task<bool>>? persist = null,
        Func<CancellationToken, Task<T>>? readPrevious = null,
        Func<T, CancellationToken, Task>? restorePrevious = null)
    {
        if (_service is null || _isBusy)
        {
            return;
        }

        _isBusy = true;
        NotifyBusy();
        T? previous = default;
        var hasPrevious = false;
        var appliedHardware = false;
        try
        {
            if (persist is not null && readPrevious is not null)
            {
                try
                {
                    previous = await readPrevious(CancellationToken.None);
                    hasPrevious = true;
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    // The write can still succeed; only the rollback value is lost.
                }
            }

            var result = await write(_service);
            apply(result);
            appliedHardware = true;
            if (persist is not null && !await persist(result, CancellationToken.None))
            {
                await RollbackAsync();
                OperationStatusText = string.Empty;
                SetError(AppStrings.Text("RazerHidSettingProfileSaveFailed"));
                return;
            }

            OperationStatusText = AppStrings.Text("AwBleHeadsetSaved");
            SetError(string.Empty);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (appliedHardware)
            {
                await RollbackAsync();
            }

            OperationStatusText = string.Empty;
            SetError(AppStrings.FormatText("AwBleHeadsetErrorWrite", exception.Message));
        }
        finally
        {
            _isBusy = false;
            NotifyBusy();
        }

        async Task RollbackAsync()
        {
            if (!hasPrevious || restorePrevious is null)
            {
                return;
            }

            try
            {
                await restorePrevious(previous!, CancellationToken.None);
                apply(previous!);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                // The original write or save error is the one the user must see.
            }
        }
    }

    private Task<bool> PersistHeadsetAsync(
        Action<HeadsetProfileSettings> mutate,
        CancellationToken cancellationToken) =>
        HeadsetProfilePersistence.SaveAsync(
            _getProfile?.Invoke(),
            _saveProfile,
            ProfileDeviceKey,
            mutate,
            cancellationToken);

    private Task<bool> PersistVolumeAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.VolumePercent = applied, cancellationToken);

    private Task<bool> PersistSidetoneAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.SideTone = applied, cancellationToken);

    private Task<bool> PersistGameModeAsync(bool applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.GameModeEnabled = applied, cancellationToken);

    private Task<bool> PersistEqPresetAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(
            headset => headset.EqPreset = applied.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

    private Task<bool> PersistAutoPowerOffAsync(int applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.AutoShutdownMinutes = applied, cancellationToken);

    private Task<bool> PersistBassAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.Bass = applied, cancellationToken);

    private Task<bool> PersistHapticsAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.HapticsStrength = applied, cancellationToken);

    private Task<bool> PersistMicEqPresetAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(
            headset => headset.MicEqPreset = applied.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

    private Task<bool> PersistCustomEqAsync(
        CustomEqSnapshot applied,
        CancellationToken cancellationToken) =>
        PersistHeadsetAsync(
            headset =>
            {
                headset.EqPreset = applied.Preset.ToString(CultureInfo.InvariantCulture);
                headset.EqBands = applied.Levels
                    .Select((level, index) => (Level: level, Frequency: CustomEqFrequencies[index]))
                    .ToDictionary(
                        item => item.Frequency.ToString(CultureInfo.InvariantCulture),
                        item => (int)item.Level,
                        StringComparer.OrdinalIgnoreCase);
            },
            cancellationToken);

    private Task<bool> PersistMicCustomEqAsync(
        CustomEqSnapshot applied,
        CancellationToken cancellationToken) =>
        PersistHeadsetAsync(
            headset =>
            {
                headset.MicEqPreset = applied.Preset.ToString(CultureInfo.InvariantCulture);
                headset.MicEqBands = applied.Levels
                    .Select((level, index) => (Level: level, Frequency: MicCustomEqFrequencies[index]))
                    .ToDictionary(
                        item => item.Frequency.ToString(CultureInfo.InvariantCulture),
                        item => (int)item.Level,
                        StringComparer.OrdinalIgnoreCase);
            },
            cancellationToken);

    private Task<bool> PersistChromaBrightnessAsync(byte applied, CancellationToken cancellationToken) =>
        PersistHeadsetAsync(headset => headset.ChromaBrightness = applied, cancellationToken);

    private Task<bool> PersistChromaEffectAsync(
        RazerAwChromaState applied,
        CancellationToken cancellationToken) =>
        PersistHeadsetAsync(
            headset =>
            {
                headset.ChromaEffect = applied.Effect.ToString();
                headset.ChromaParameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                headset.ChromaParameters["primary"] = Convert.ToHexString(applied.PrimaryColor);
                headset.ChromaParameters["secondary"] = Convert.ToHexString(applied.SecondaryColor);
            },
            cancellationToken);

    private readonly record struct CustomEqSnapshot(byte Preset, IReadOnlyList<sbyte> Levels);
}
