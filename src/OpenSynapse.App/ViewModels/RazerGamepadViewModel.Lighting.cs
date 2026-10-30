using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerGamepadViewModel
{
    private static readonly byte[] EncodableLightingEffects = [0, 1, 2, 3, 5, 13];

    private IReadOnlyList<RazerGamepadLightingRegionState> _lightingRegions = [];
    private int _selectedLightingRegionIndex;
    private int _selectedLightingEffectIndex = -1;
    private int _lightingSpeedIndex;
    private bool _hasLighting;
    private string _lightingStatusText = string.Empty;
    private Color _lightingColor = Color.FromArgb(255, 0, 255, 102);
    private double _lightingBrightness = 100;

    public Visibility LightingVisibility => _hasLighting ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightingEffectVisibility =>
        LightingEffectIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightingColorVisibility =>
        SelectedLightingEffect is 1 or 2 or 5 or 13 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightingSpeedVisibility =>
        SelectedLightingEffect == 5 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightingBrightnessVisibility =>
        CurrentLightingRegion?.Brightness is not null ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> LightingRegionOptions =>
        _lightingRegions.Select(item => $"Region {item.Region.RegionId}").ToArray();

    public int SelectedLightingRegionIndex
    {
        get => _selectedLightingRegionIndex;
        set
        {
            if (!SetField(ref _selectedLightingRegionIndex,
                    Math.Clamp(value, 0, Math.Max(0, _lightingRegions.Count - 1))))
            {
                return;
            }

            RefreshLightingSelection();
        }
    }

    public IReadOnlyList<string> LightingEffectOptions =>
        LightingEffectIds.Select(EffectLabel).ToArray();

    public int SelectedLightingEffectIndex
    {
        get => _selectedLightingEffectIndex;
        set
        {
            if (!SetField(ref _selectedLightingEffectIndex,
                    Math.Clamp(value, -1, Math.Max(-1, LightingEffectIds.Count - 1))))
            {
                return;
            }

            OnPropertyChanged(nameof(LightingColorVisibility));
            OnPropertyChanged(nameof(LightingSpeedVisibility));
        }
    }

    public IReadOnlyList<string> LightingSpeedOptions =>
    [
        AppStrings.Text("BladeReactiveSpeedLevel1"),
        AppStrings.Text("BladeReactiveSpeedLevel2"),
        AppStrings.Text("BladeReactiveSpeedLevel3"),
        AppStrings.Text("BladeReactiveSpeedLevel4"),
    ];

    public int LightingSpeedIndex
    {
        get => _lightingSpeedIndex;
        set => SetField(ref _lightingSpeedIndex, Math.Clamp(value, 0, 3));
    }

    public Color LightingColor
    {
        get => _lightingColor;
        set => SetField(ref _lightingColor, value);
    }

    public double LightingBrightness
    {
        get => _lightingBrightness;
        set
        {
            if (!SetField(ref _lightingBrightness, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(LightingBrightnessText));
        }
    }

    public string LightingBrightnessText => $"{Math.Round(LightingBrightness)}%";
    public string LightingStatusText
    {
        get => _lightingStatusText;
        private set => SetField(ref _lightingStatusText, value);
    }

    public async Task ApplyLightingAsync(CancellationToken cancellationToken = default)
    {
        var region = CurrentLightingRegion;
        if (region is null || !BeginOperation()) return;
        try
        {
            if (LightingEffectIds.Count > 0)
            {
                var effectId = LightingEffectIds[Math.Clamp(
                    SelectedLightingEffectIndex, 0, LightingEffectIds.Count - 1)];
                var (flags, rate, colors) = CreateEffectPayload(effectId);
                await _service.SetLightingEffectAsync(
                    region.Region.RegionId,
                    effectId,
                    flags,
                    rate,
                    colors,
                    cancellationToken);
            }

            if (region.Brightness is not null)
            {
                await _service.SetLightingBrightnessAsync(
                    region.Region.RegionId,
                    (byte)Math.Round(LightingBrightness),
                    cancellationToken);
            }

            await LoadLightingCoreAsync(cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RazerGamepadLightingRegionState? CurrentLightingRegion =>
        _lightingRegions.Count == 0
            ? null
            : _lightingRegions[Math.Clamp(_selectedLightingRegionIndex, 0, _lightingRegions.Count - 1)];

    private IReadOnlyList<byte> LightingEffectIds =>
        CurrentLightingRegion?.EffectIds.Where(IsEncodableEffect).ToArray() ?? [];

    private byte? SelectedLightingEffect =>
        LightingEffectIds.Count == 0
            ? null
            : LightingEffectIds[Math.Clamp(_selectedLightingEffectIndex, 0, LightingEffectIds.Count - 1)];

    private async Task LoadLightingCoreAsync(CancellationToken cancellationToken)
    {
        var state = await _service.ReadLightingAsync(cancellationToken);
        _lightingRegions = state.Regions;
        _hasLighting = _lightingRegions.Any(item =>
            item.EffectIds.Count > 0 || item.Brightness is not null);
        _selectedLightingRegionIndex = Math.Clamp(
            _selectedLightingRegionIndex,
            0,
            Math.Max(0, _lightingRegions.Count - 1));
        LightingStatusText = state.Errors.Count == 0
            ? string.Empty
            : string.Join(", ", state.Errors);
        OnPropertyChanged(nameof(LightingVisibility));
        OnPropertyChanged(nameof(LightingRegionOptions));
        RefreshLightingSelection();
    }

    private void RefreshLightingSelection()
    {
        var region = CurrentLightingRegion;
        var effectIds = LightingEffectIds;
        var currentEffect = region?.Effect?.EffectId;
        var effectIndex = currentEffect is byte effectId
            ? effectIds.ToList().IndexOf(effectId)
            : 0;
        _selectedLightingEffectIndex = effectIds.Count == 0 ? -1 : Math.Max(0, effectIndex);
        if (region?.Effect?.Colors.Count > 0)
        {
            var color = region.Effect.Colors[0];
            _lightingColor = Color.FromArgb(255, color.Red, color.Green, color.Blue);
        }
        if (region?.Brightness is byte brightness)
        {
            _lightingBrightness = Math.Min(100, Math.Ceiling(brightness * 100d / 255d));
        }

        OnPropertyChanged(nameof(SelectedLightingRegionIndex));
        OnPropertyChanged(nameof(LightingEffectOptions));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(LightingSpeedVisibility));
        OnPropertyChanged(nameof(LightingBrightnessVisibility));
        OnPropertyChanged(nameof(LightingColor));
        OnPropertyChanged(nameof(LightingBrightness));
        OnPropertyChanged(nameof(LightingBrightnessText));
    }

    private (byte Flags, byte Rate, IReadOnlyList<RazerRgb> Colors) CreateEffectPayload(byte effectId)
    {
        var color = new RazerRgb(LightingColor.R, LightingColor.G, LightingColor.B);
        return effectId switch
        {
            0 or 3 => (0, 0, []),
            2 => (1, 0, [color]),
            5 => (0, (byte)(LightingSpeedIndex + 1), [color]),
            _ => (0, 0, [color]),
        };
    }

    private static bool IsEncodableEffect(byte effectId) =>
        EncodableLightingEffects.Contains(effectId);

    private static string EffectLabel(byte effectId) => effectId switch
    {
        0 => AppStrings.Text("Text_39B523BD"),
        1 => AppStrings.Text("Text_8FF9138F"),
        2 => AppStrings.Text("Text_7EDA32B9"),
        3 => AppStrings.Text("Text_21BE26F0"),
        5 => AppStrings.Text("Text_5DCB722E"),
        13 => AppStrings.Text("RazerGamepadLightingEffectStaticExtended"),
        _ => $"Effect 0x{effectId:X2}",
    };
}
