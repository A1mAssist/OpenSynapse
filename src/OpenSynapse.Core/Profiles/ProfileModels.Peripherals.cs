using System.Text.Json;

namespace OpenSynapse.Core.Profiles;

/// <summary>Chroma controls for devices that use the shared class 0x10 channel.</summary>
public sealed class ChromaProfileSettings
{
    public int? BrightnessPercent { get; set; }
    public string? Effect { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public Dictionary<string, string>? ArgbFrames { get; set; }

    internal void ApplySafeDefaults()
    {
        MouseProfileSettings.ValidatePercent(BrightnessPercent, nameof(BrightnessPercent));
        ArgbFrames = ArgbFrames is null
            ? null
            : ProfileDictionary.Normalize(ArgbFrames);
    }

    internal ChromaProfileSettings Clone() => new()
    {
        BrightnessPercent = BrightnessPercent,
        Effect = Effect,
        PrimaryColor = PrimaryColor,
        SecondaryColor = SecondaryColor,
        ArgbFrames = ArgbFrames is null
            ? null
            : new Dictionary<string, string>(ArgbFrames, StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>The 053E mixer's persisted mute and brightness state.</summary>
public sealed class MixerProfileSettings
{
    public List<bool>? ChannelMutes { get; set; }
    public bool? MicMute { get; set; }
    public int? BrightnessPercent { get; set; }

    internal void ApplySafeDefaults()
    {
        if (ChannelMutes is { Count: not 4 })
        {
            throw new InvalidDataException("Mixer channel mutes must contain exactly four channels.");
        }

        MouseProfileSettings.ValidatePercent(BrightnessPercent, nameof(BrightnessPercent));
    }

    internal MixerProfileSettings Clone() => new()
    {
        ChannelMutes = ChannelMutes?.ToList(),
        MicMute = MicMute,
        BrightnessPercent = BrightnessPercent,
    };
}

/// <summary>Kiyo white-balance state. The current native service only toggles auto mode.</summary>
public sealed class KiyoProfileSettings
{
    public bool? WhiteBalanceAuto { get; set; }
    public int? WhiteBalanceTemperature { get; set; }

    internal void ApplySafeDefaults()
    {
        if (WhiteBalanceTemperature is < 0)
        {
            throw new InvalidDataException("Kiyo white-balance temperature cannot be negative.");
        }
    }

    internal KiyoProfileSettings Clone() => new()
    {
        WhiteBalanceAuto = WhiteBalanceAuto,
        WhiteBalanceTemperature = WhiteBalanceTemperature,
    };
}

/// <summary>
/// One persisted gamepad button assignment. Data holds the encoded block without
/// padding: its count is the size byte the protocol writes.
/// </summary>
public sealed class GamepadButtonAssignmentProfile
{
    public byte ButtonId { get; set; }
    public byte Mode { get; set; }
    public byte Function { get; set; }
    public List<byte>? Data { get; set; }

    internal void ApplySafeDefaults()
    {
        if (ButtonId == 0)
        {
            throw new InvalidDataException("Gamepad button-assignment IDs start at 1.");
        }
        if (Mode > 1)
        {
            throw new InvalidDataException("Gamepad button-assignment mode must be 0 or 1.");
        }
        if (Data is { Count: > 5 })
        {
            throw new InvalidDataException("Gamepad button-assignment data is limited to five bytes.");
        }
    }

    internal GamepadButtonAssignmentProfile Clone() => new()
    {
        ButtonId = ButtonId,
        Mode = Mode,
        Function = Function,
        Data = Data?.ToList(),
    };
}

/// <summary>Per-trigger analog range or digital actuation state.</summary>
public sealed class GamepadTriggerProfileSettings
{
    public string? Mode { get; set; }
    public int? StartPercent { get; set; }
    public int? EndPercent { get; set; }
    public int? ActuationIndex { get; set; }
    public bool? RapidTrigger { get; set; }

    internal void ApplySafeDefaults()
    {
        MouseProfileSettings.ValidatePercent(StartPercent, nameof(StartPercent));
        MouseProfileSettings.ValidatePercent(EndPercent, nameof(EndPercent));
        if (StartPercent is { } start && EndPercent is { } end && start > end)
        {
            throw new InvalidDataException("Gamepad trigger range start must not exceed its end.");
        }
        MouseProfileSettings.ValidateRange(ActuationIndex, 0, 6, nameof(ActuationIndex));
    }

    internal GamepadTriggerProfileSettings Clone() => new()
    {
        Mode = Mode,
        StartPercent = StartPercent,
        EndPercent = EndPercent,
        ActuationIndex = ActuationIndex,
        RapidTrigger = RapidTrigger,
    };
}

/// <summary>
/// Gamepad settings the native protocol-25 service actually exposes. TriggerMode
/// stays as the legacy both-sides fallback for profiles written before the
/// per-trigger objects.
/// </summary>
public sealed class GamepadProfileSettings
{
    public int? PollingRateHertz { get; set; }
    public int? VibrationStrengthPercent { get; set; }
    public string? MappingPreset { get; set; }
    public double? LeftStickDeadzone { get; set; }
    public double? RightStickDeadzone { get; set; }
    public int? LeftStickSensitivity { get; set; }
    public int? RightStickSensitivity { get; set; }
    public string? LeftStickCircularity { get; set; }
    public string? RightStickCircularity { get; set; }
    public bool? LeftStickPreventDoubleDeadzone { get; set; }
    public bool? RightStickPreventDoubleDeadzone { get; set; }
    public string? TriggerMode { get; set; }
    public GamepadTriggerProfileSettings? LeftTrigger { get; set; }
    public GamepadTriggerProfileSettings? RightTrigger { get; set; }
    public string? DpadMode { get; set; }
    public bool? IgnoreDpadDiagonals { get; set; }
    public int? SocdModeIndex { get; set; }
    public List<GamepadButtonAssignmentProfile>? ButtonAssignments { get; set; }
    public bool? LightingEnabled { get; set; }
    public string? LightingEffect { get; set; }
    public Dictionary<string, string>? LightingParameters { get; set; }

    internal void ApplySafeDefaults()
    {
        if (PollingRateHertz is <= 0)
        {
            throw new InvalidDataException("Gamepad polling rate must be positive.");
        }
        MouseProfileSettings.ValidatePercent(VibrationStrengthPercent, nameof(VibrationStrengthPercent));
        ValidateDeadzone(LeftStickDeadzone, nameof(LeftStickDeadzone));
        ValidateDeadzone(RightStickDeadzone, nameof(RightStickDeadzone));
        MouseProfileSettings.ValidatePercent(LeftStickSensitivity, nameof(LeftStickSensitivity));
        MouseProfileSettings.ValidatePercent(RightStickSensitivity, nameof(RightStickSensitivity));
        LeftTrigger?.ApplySafeDefaults();
        RightTrigger?.ApplySafeDefaults();
        MouseProfileSettings.ValidateRange(SocdModeIndex, 0, 4, nameof(SocdModeIndex));
        ValidateButtonAssignments(ButtonAssignments);
        LightingParameters = LightingParameters is null
            ? null
            : ProfileDictionary.Normalize(LightingParameters);
    }

    private static void ValidateDeadzone(double? value, string name)
    {
        if (value is < 0 or > 100 || (value is { } finite && !double.IsFinite(finite)))
        {
            throw new InvalidDataException($"{name} must be between 0 and 100.");
        }
    }

    private static void ValidateButtonAssignments(List<GamepadButtonAssignmentProfile>? assignments)
    {
        if (assignments is null)
        {
            return;
        }
        foreach (var assignment in assignments)
        {
            if (assignment is null)
            {
                throw new InvalidDataException("Gamepad button assignments must not contain null entries.");
            }
            assignment.ApplySafeDefaults();
        }
        if (assignments.Select(item => (item.ButtonId, item.Mode)).Distinct().Count() != assignments.Count)
        {
            throw new InvalidDataException("Gamepad button assignments must use each button and mode at most once.");
        }
    }
}

/// <summary>
/// Saves one peripheral section and restores the complete device entry if the
/// profile store rejects the write. Keeping the entire entry preserves sections
/// owned by other device pages on the same VID/PID.
/// </summary>
public static class PeripheralProfileStore
{
    public static async Task<bool> SaveAsync(
        ProfileDocument? document,
        Func<CancellationToken, Task<bool>>? save,
        string deviceKey,
        Action<DeviceProfileSettings> mutate,
        CancellationToken cancellationToken)
    {
        if (document is null || save is null)
        {
            return true;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(mutate);

        var created = !document.Devices.TryGetValue(deviceKey, out var device);
        device ??= new DeviceProfileSettings();
        var previous = JsonSerializer.SerializeToNode(device);
        mutate(device);
        if (created)
        {
            document.Devices[deviceKey] = device;
        }

        try
        {
            if (await save(cancellationToken))
            {
                return true;
            }
        }
        catch
        {
            Restore();
            throw;
        }

        Restore();
        return false;

        void Restore()
        {
            if (created)
            {
                document.Devices.Remove(deviceKey);
                return;
            }

            document.Devices[deviceKey] =
                previous?.Deserialize<DeviceProfileSettings>() ?? new DeviceProfileSettings();
        }
    }
}
