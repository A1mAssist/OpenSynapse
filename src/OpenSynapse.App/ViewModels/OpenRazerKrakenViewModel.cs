using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerKrakenViewModel : INotifyPropertyChanged
{
    private readonly OpenRazerSpecialLightingService _service;
    private readonly HashSet<OpenRazerLightingEffect> _unsupported = [];
    private OpenRazerLightingEffect _selectedEffect;
    private Color _primary = Color.FromArgb(255, 0, 255, 102);
    private Color _secondary = Color.FromArgb(255, 0, 153, 255);
    private Color _tertiary = Color.FromArgb(255, 255, 0, 153);
    private byte _intensity = 255;
    private bool _isBusy;
    private bool _requiresRescan;
    private string _errorText;

    public OpenRazerKrakenViewModel(
        OpenRazerSpecialLightingService service,
        OpenRazerSpecialLightingConnection connection)
    {
        _service = service;
        Connection = connection;
        _errorText = connection.Error ?? string.Empty;
        _selectedEffect = Effects.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public OpenRazerSpecialLightingConnection Connection { get; }
    public string InstanceId => Connection.InstanceId;
    public string Name => Connection.DisplayName;
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string StatusText => RequiresRescan
        ? AppStrings.Text("Text_C0E6F3C9")
        : Connection.IsReady ? AppStrings.Text("Text_C097B416") : AppStrings.Text("Text_242E08F4");
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool RequiresRescan { get => _requiresRescan; private set => SetField(ref _requiresRescan, value); }
    public IReadOnlyList<OpenRazerLightingEffect> Effects => Connection.SupportedEffects
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
    public OpenRazerLightingEffect SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (!Effects.Contains(value) || !SetField(ref _selectedEffect, value)) return;
            OnPropertyChanged(nameof(SelectedEffectIndex));
            OnPropertyChanged(nameof(PrimaryVisibility));
            OnPropertyChanged(nameof(SecondaryVisibility));
            OnPropertyChanged(nameof(TertiaryVisibility));
            OnPropertyChanged(nameof(IntensityVisibility));
            OnPropertyChanged(nameof(CanApply));
        }
    }
    public Color Primary { get => _primary; set => SetField(ref _primary, value); }
    public Color Secondary { get => _secondary; set => SetField(ref _secondary, value); }
    public Color Tertiary { get => _tertiary; set => SetField(ref _tertiary, value); }
    public byte Intensity { get => _intensity; set => SetField(ref _intensity, value); }
    public string IntensityText => $"{Math.Round(Intensity / 255d * 100)}%";
    public Visibility PrimaryVisibility => VisibleWhen(SelectedEffect is
        OpenRazerLightingEffect.Static or OpenRazerLightingEffect.BreathingSingle or
        OpenRazerLightingEffect.BreathingDual or OpenRazerLightingEffect.BreathingTriple or
        OpenRazerLightingEffect.Custom);
    public Visibility SecondaryVisibility => VisibleWhen(SelectedEffect is
        OpenRazerLightingEffect.BreathingDual or OpenRazerLightingEffect.BreathingTriple);
    public Visibility TertiaryVisibility => VisibleWhen(SelectedEffect == OpenRazerLightingEffect.BreathingTriple);
    public Visibility IntensityVisibility => VisibleWhen(SelectedEffect is
        OpenRazerLightingEffect.Static or OpenRazerLightingEffect.Custom);
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public bool CanApply => Connection.IsReady && !RequiresRescan && !IsBusy && Effects.Contains(SelectedEffect);

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        var effect = SelectedEffect;
        IsBusy = true;
        OnPropertyChanged(nameof(CanApply));
        try
        {
            await _service.SetKrakenLightingAsync(Connection, new OpenRazerLightingSettings(
                effect, Primary: ToColor(Primary), Secondary: ToColor(Secondary), Tertiary: ToColor(Tertiary)),
                effect is OpenRazerLightingEffect.Static or OpenRazerLightingEffect.Custom ? Intensity : null,
                cancellationToken);
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (NotSupportedException exception)
        {
            _unsupported.Add(effect);
            ErrorText = exception.Message;
            OnPropertyChanged(nameof(Effects));
            OnPropertyChanged(nameof(EffectOptions));
            _selectedEffect = Effects.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedEffect));
            OnPropertyChanged(nameof(SelectedEffectIndex));
            OnPropertyChanged(nameof(PrimaryVisibility));
            OnPropertyChanged(nameof(SecondaryVisibility));
            OnPropertyChanged(nameof(TertiaryVisibility));
            OnPropertyChanged(nameof(IntensityVisibility));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            ErrorText = exception.Message;
            RequiresRescan = true;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private static Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static OpenRazerColor ToColor(Color color) => new(color.R, color.G, color.B);
    private static string FormatEffect(OpenRazerLightingEffect effect) => AppStrings.Text($"OpenRazerEffect{effect}");
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
        }
        if (name == nameof(Intensity)) OnPropertyChanged(nameof(IntensityText));
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));
}
