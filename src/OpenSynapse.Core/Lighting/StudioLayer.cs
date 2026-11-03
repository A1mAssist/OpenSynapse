using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenSynapse.Core.Lighting;

public enum StudioEffectKind
{
    Solid,
    Breathing,
    Spectrum,
    Wave,
    Gradient,
    Fire,
    Reactive,
    Ripple,
    Starlight,
    Ambient,
    Audio,
    Wheel,
    ChromaGenerate,
    Tidal,
}

public enum StudioBlendMode
{
    Normal,
    Add,
    Multiply,
    Screen,
}

/// <summary>
/// One studio layer. Values are intentionally mutable and observable so the
/// inspector can bind directly while the compositor takes immutable snapshots.
/// </summary>
public sealed class StudioLayer : INotifyPropertyChanged
{
    private string _name;
    private StudioEffectKind _effect;
    private bool _enabled = true;
    private double _opacity = 100;
    private StudioBlendMode _blend;
    private StudioRgb _primary = new(0x44, 0xD6, 0x2C);
    private StudioRgb _secondary = new(0x00, 0xBF, 0xFF);
    private double _speed = 1;

    public StudioLayer()
        : this("Layer")
    {
    }

    public StudioLayer(string name)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, string.IsNullOrWhiteSpace(value) ? "Layer" : value.Trim())) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    /// <summary>Read-only alias used by item templates that cannot bind to a mutable name.</summary>
    public string DisplayName => _name;

    public StudioEffectKind Effect
    {
        get => _effect;
        set => Set(ref _effect, Enum.IsDefined(value) ? value : StudioEffectKind.Solid);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>Percent, 0..100.</summary>
    public double Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(value, 0, 100));
    }

    public StudioBlendMode Blend
    {
        get => _blend;
        set => Set(ref _blend, Enum.IsDefined(value) ? value : StudioBlendMode.Normal);
    }

    public StudioRgb Primary
    {
        get => _primary;
        set => Set(ref _primary, value);
    }

    public StudioRgb Secondary
    {
        get => _secondary;
        set => Set(ref _secondary, value);
    }

    /// <summary>Animation speed multiplier, 0.1..5.</summary>
    public double Speed
    {
        get => _speed;
        set => Set(ref _speed, Math.Clamp(value, 0.1, 5));
    }

    public StudioLayerSnapshot ToSnapshot() =>
        new(Effect, Opacity, Blend, Primary, Secondary, Speed);

    public StudioLayer Clone() => new(Name)
    {
        Id = Id,
        Effect = Effect,
        Enabled = Enabled,
        Opacity = Opacity,
        Blend = Blend,
        Primary = Primary,
        Secondary = Secondary,
        Speed = Speed,
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>Immutable render input; safe to read from the hardware frame thread.</summary>
public readonly record struct StudioLayerSnapshot(
    StudioEffectKind Effect,
    double Opacity,
    StudioBlendMode Blend,
    StudioRgb Primary,
    StudioRgb Secondary,
    double Speed);
