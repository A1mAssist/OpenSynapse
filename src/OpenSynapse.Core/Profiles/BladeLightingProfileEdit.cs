using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

/// <summary>A lighting edit belongs to the profile and power source selected at request time.</summary>
public sealed class BladeLightingProfileEdit
{
    private readonly ProfileDocument _document;
    private readonly ProfileDefinition _profile;
    private readonly string _profileName;

    public BladeLightingProfileEdit(ProfileDocument document, PowerSourceKind powerSource)
    {
        _document = document;
        _profileName = document.ActiveProfileName;
        _profile = document.Profiles.TryGetValue(_profileName, out var profile)
            ? profile : throw new InvalidOperationException(
                "The active profile is missing from the profile catalog.");
        PowerSource = powerSource;
        var power = powerSource switch
        {
            PowerSourceKind.FullPowerAdapter => _profile.PluggedIn,
            PowerSourceKind.LowPowerAdapter => _profile.LowPowerAdapter,
            PowerSourceKind.Battery => _profile.OnBattery,
            _ => null,
        };
        Blade = power?.Blade ?? _profile.Global.Blade;
        Lighting = power?.Lighting ?? _profile.Global.Lighting;
    }

    public PowerSourceKind PowerSource { get; }
    public BladeProfileSettings Blade { get; }
    public LightingProfile Lighting { get; }

    public bool IsAttached(ProfileDocument document) =>
        ReferenceEquals(document, _document) &&
        document.Profiles.TryGetValue(_profileName, out var profile) &&
        ReferenceEquals(profile, _profile);

    public bool CanWriteHardware(ProfileDocument document, PowerSourceKind powerSource, bool displayAvailable) =>
        displayAvailable && IsAttached(document) &&
        StringComparer.OrdinalIgnoreCase.Equals(document.ActiveProfileName, _profileName) &&
        powerSource == PowerSource;

    public bool CanWriteEffect(ProfileDocument document, PowerSourceKind powerSource,
        bool displayAvailable, bool lightingEnabled) =>
        lightingEnabled && (Blade.LightingEnabled ?? _profile.Global.Blade.LightingEnabled ?? true) &&
        CanWriteHardware(document, powerSource, displayAvailable);

    public async Task SaveBrightnessAsync(ProfileDocument document, byte brightness,
        Func<CancellationToken, Task<bool>> save, CancellationToken cancellationToken, string failureMessage)
    {
        EnsureAttached(document);
        var previous = Blade.KeyboardBrightness;
        Blade.KeyboardBrightness = brightness;
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            if (Blade.KeyboardBrightness == brightness) Blade.KeyboardBrightness = previous;
            throw;
        }
    }

    public async Task SaveEffectAsync(ProfileDocument document, LightingProfile encoded,
        Func<CancellationToken, Task<bool>> save, CancellationToken cancellationToken, string failureMessage)
    {
        EnsureAttached(document);
        var previousEffect = Lighting.Effect;
        var previousParameters = Lighting.Parameters;
        Lighting.Effect = encoded.Effect;
        Lighting.Parameters = encoded.Parameters;
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            if (ReferenceEquals(Lighting.Parameters, encoded.Parameters))
            {
                Lighting.Effect = previousEffect;
                Lighting.Parameters = previousParameters;
            }
            throw;
        }
    }

    private void EnsureAttached(ProfileDocument document)
    {
        if (!IsAttached(document))
            throw new InvalidOperationException(
                "The profile changed before the lighting edit could be saved. Retry the edit.");
    }
}
