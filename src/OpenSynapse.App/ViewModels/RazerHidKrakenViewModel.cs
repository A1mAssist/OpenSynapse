using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidKrakenViewModel : INotifyPropertyChanged
{
    private readonly RazerHidSpecialLightingService _service;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private readonly HashSet<RazerHidLightingEffect> _unsupported = [];
    private RazerHidLightingEffect _selectedEffect;
    private Color _primary = Color.FromArgb(255, 0, 255, 102);
    private Color _secondary = Color.FromArgb(255, 0, 153, 255);
    private Color _tertiary = Color.FromArgb(255, 255, 0, 153);
    private double _intensity = 255;
    private bool _isBusy;
    private bool _requiresRescan;
    private string _errorText;
    private string? _serial;

    public RazerHidKrakenViewModel(
        RazerHidSpecialLightingService service,
        RazerHidSpecialLightingConnection connection,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        _service = service;
        _getProfile = getProfile;
        _saveProfile = saveProfile;
        Connection = connection;
        _errorText = string.IsNullOrWhiteSpace(connection.Error)
            ? string.Empty
            : AppStrings.Text("RazerHidProtocolRescanRequired");
        _selectedEffect = Effects.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerHidSpecialLightingConnection Connection { get; }
    public string InstanceId => Connection.InstanceId;
    public string Name => Connection.DisplayName;
    public string CategoryText => (Connection.Kind is
        RazerHidSpecialLightingKind.Kraken37 or RazerHidSpecialLightingKind.KrakenV4)
            ? AppStrings.Text("RazerHidKrakenSubtitle/Text")
            : AppStrings.Text("Text_71E692AA");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string? Serial { get => _serial; private set => SetField(ref _serial, value); }
    public Visibility SerialVisibility => string.IsNullOrWhiteSpace(Serial) ? Visibility.Collapsed : Visibility.Visible;

    public async Task LoadSerialAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Serial = await _service.TryReadSerialAsync(Connection, cancellationToken);
            await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);
            OnPropertyChanged(nameof(SerialVisibility));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
    public string StatusText => RequiresRescan
        ? AppStrings.Text("Text_C0E6F3C9")
        : Connection.IsReady ? AppStrings.Text("Text_C097B416") : AppStrings.Text("Text_242E08F4");
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool RequiresRescan { get => _requiresRescan; private set => SetField(ref _requiresRescan, value); }
    public Visibility ControlsVisibility => VisibleWhen(Connection.IsReady && !RequiresRescan &&
        (Connection.Kind is RazerHidSpecialLightingKind.Kraken37 or RazerHidSpecialLightingKind.KrakenV4) &&
        Effects.Count > 0);
    public IReadOnlyList<RazerHidLightingEffect> Effects => Connection.SupportedEffects
        .Where(effect => !_unsupported.Contains(effect)).Order().ToArray();
    public IReadOnlyList<string> EffectOptions => Effects.Select(FormatEffect).ToArray();
    public int SelectedEffectIndex
    {
        get => IndexOf(Effects, SelectedEffect);
        set
        {
            if (value >= 0 && value < Effects.Count) SelectedEffect = Effects[value];
        }
    }
    public RazerHidLightingEffect SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (!Effects.Contains(value) || !SetField(ref _selectedEffect, value)) return;
            OnPropertyChanged(nameof(SelectedEffectIndex));
            OnPropertyChanged(nameof(PrimaryVisibility));
            OnPropertyChanged(nameof(LightingPresetVisibility));
            OnPropertyChanged(nameof(SecondaryVisibility));
            OnPropertyChanged(nameof(TertiaryVisibility));
            OnPropertyChanged(nameof(IntensityVisibility));
            OnPropertyChanged(nameof(CanApply));
        }
    }
    public Color Primary { get => _primary; set => SetField(ref _primary, value); }
    public Color Secondary { get => _secondary; set => SetField(ref _secondary, value); }
    public Color Tertiary { get => _tertiary; set => SetField(ref _tertiary, value); }
    public double Intensity { get => _intensity; set => SetField(ref _intensity, value); }
    public string IntensityText => $"{Math.Round(Intensity / 255d * 100)}%";
    public Visibility LightingPresetVisibility => SecondaryVisibility;
    public Visibility PrimaryVisibility => VisibleWhen(SelectedEffect is
        RazerHidLightingEffect.BreathingSingle or
        RazerHidLightingEffect.BreathingDual or RazerHidLightingEffect.BreathingTriple or
        RazerHidLightingEffect.Custom ||
        SelectedEffect == RazerHidLightingEffect.Static && Connection.SupportsStaticColor);
    public Visibility SecondaryVisibility => VisibleWhen(SelectedEffect is
        RazerHidLightingEffect.BreathingDual or RazerHidLightingEffect.BreathingTriple);
    public Visibility TertiaryVisibility => VisibleWhen(SelectedEffect == RazerHidLightingEffect.BreathingTriple);
    public Visibility IntensityVisibility => VisibleWhen(SelectedEffect is
        RazerHidLightingEffect.Custom ||
        SelectedEffect == RazerHidLightingEffect.Static && Connection.SupportsStaticColor);
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public bool CanApply => Connection.IsReady && !RequiresRescan && !IsBusy && Effects.Contains(SelectedEffect);

    private string ProfileDeviceKey => string.IsNullOrWhiteSpace(Serial)
        ? $"1532:{Connection.ProductId:X4}"
        : Serial;

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey,
            Name,
            0x1532,
            Connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            0,
            1,
            2,
            "kraken",
            DeviceCategory.Headset);
        var applier = new HeadsetPeripheralProfileApplier(
            resolveKraken: (_, _) => Task.FromResult<(
                RazerHidSpecialLightingService Service,
                RazerHidSpecialLightingConnection Connection)?>((_service, Connection)));
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(
                document,
                descriptor,
                powerSource,
                ProfileResolver.Resolve(document, descriptor, powerSource)),
            cancellationToken);
    }

    [ProfileSetting("headset.chroma-effect", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        var effect = SelectedEffect;
        var previous = new LightingSnapshot(
            SelectedEffect,
            Primary,
            Secondary,
            Tertiary,
            Intensity);
        IsBusy = true;
        OnPropertyChanged(nameof(CanApply));
        try
        {
            var intensity = effect is RazerHidLightingEffect.Static or RazerHidLightingEffect.Custom
                ? (byte)Math.Clamp(Math.Round(Intensity), 0, 255)
                : (byte?)null;
            await _service.SetKrakenLightingAsync(Connection, new RazerHidLightingSettings(
                effect, Primary: ToColor(Primary), Secondary: ToColor(Secondary), Tertiary: ToColor(Tertiary)),
                intensity,
                cancellationToken);
            if (!await PersistHeadsetAsync(headset =>
                {
                    headset.ChromaEffect = effect.ToString();
                    headset.ChromaBrightness = (int)Math.Round(Intensity / 255d * 100d);
                    headset.ChromaParameters = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["primary"] = $"{Primary.R:X2}{Primary.G:X2}{Primary.B:X2}",
                        ["secondary"] = $"{Secondary.R:X2}{Secondary.G:X2}{Secondary.B:X2}",
                        ["tertiary"] = $"{Tertiary.R:X2}{Tertiary.G:X2}{Tertiary.B:X2}",
                        ["intensity"] = Math.Round(Intensity).ToString(CultureInfo.InvariantCulture),
                    };
                },
                cancellationToken))
            {
                ApplyLighting(previous);
                try
                {
                    await _service.SetKrakenLightingAsync(
                        Connection,
                        BuildSettings(previous),
                        previous.Intensity is >= 0 and <= 255 ? (byte)previous.Intensity : null,
                        CancellationToken.None);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    // The save failure is the actionable error; a restore failure only loses the old look.
                }

                ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
                return;
            }

            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (NotSupportedException)
        {
            _unsupported.Add(effect);
            ErrorText = AppStrings.Text("RazerHidProtocolUnsupported");
            OnPropertyChanged(nameof(Effects));
            OnPropertyChanged(nameof(ControlsVisibility));
            OnPropertyChanged(nameof(EffectOptions));
            _selectedEffect = Effects.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedEffect));
            OnPropertyChanged(nameof(SelectedEffectIndex));
            OnPropertyChanged(nameof(PrimaryVisibility));
            OnPropertyChanged(nameof(LightingPresetVisibility));
            OnPropertyChanged(nameof(SecondaryVisibility));
            OnPropertyChanged(nameof(TertiaryVisibility));
            OnPropertyChanged(nameof(IntensityVisibility));
        }
        catch (RazerDeviceTimeoutException)
        {
            // A silent device is transient; keep the controls available for a retry.
            ErrorText = AppStrings.Text("RazerHidProtocolQueryFailed");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            ErrorText = AppStrings.Text("RazerHidProtocolRescanRequired");
            RequiresRescan = true;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private Task<bool> PersistHeadsetAsync(
        Action<HeadsetProfileSettings> mutate,
        CancellationToken cancellationToken) =>
        HeadsetProfilePersistence.SaveAsync(
            _getProfile?.Invoke(),
            _saveProfile,
            ProfileDeviceKey,
            mutate,
            cancellationToken);

    private static RazerHidLightingSettings BuildSettings(LightingSnapshot state) =>
        new(
            state.Effect,
            Primary: ToColor(state.Primary),
            Secondary: ToColor(state.Secondary),
            Tertiary: ToColor(state.Tertiary));

    private void ApplyLighting(LightingSnapshot state)
    {
        SelectedEffect = state.Effect;
        Primary = state.Primary;
        Secondary = state.Secondary;
        Tertiary = state.Tertiary;
        Intensity = state.Intensity;
    }

    private static Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static RazerHidColor ToColor(Color color) => new(color.R, color.G, color.B);
    private static string FormatEffect(RazerHidLightingEffect effect) => AppStrings.Text($"RazerHidEffect{effect}");
    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        return -1;
    }
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name == nameof(ErrorText)) OnPropertyChanged(nameof(HasError));
        if (name == nameof(RequiresRescan))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ControlsVisibility));
        }
        if (name == nameof(Intensity)) OnPropertyChanged(nameof(IntensityText));
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));

    private readonly record struct LightingSnapshot(
        RazerHidLightingEffect Effect,
        Color Primary,
        Color Secondary,
        Color Tertiary,
        double Intensity);
}
