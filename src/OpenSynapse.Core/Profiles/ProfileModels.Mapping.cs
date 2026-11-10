namespace OpenSynapse.Core.Profiles;

/// <summary>One persisted button action; transport-specific values live in Extra.</summary>
public sealed class ButtonMappingProfile
{
    public byte ButtonId { get; set; }
    public byte? Mode { get; set; }
    public string? Group { get; set; }
    public byte? FunctionId { get; set; }
    public List<byte>? FunctionData { get; set; }
    public int? OptionIndex { get; set; }
    public int? ModifierIndex { get; set; }
    public double? TurboRate { get; set; }
    public double? ProfileId { get; set; }
    public double? DpiClutchX { get; set; }
    public double? DpiClutchY { get; set; }
    public int? MacroPlaybackIndex { get; set; }
    public int? MacroTargetIndex { get; set; }
    public int? SoftwareTriggerModeIndex { get; set; }
    public double? MacroRepeatCount { get; set; }
    public int? SelectedMacroSlotIndex { get; set; }
    public Dictionary<string, string>? Extra { get; set; }

    internal void ApplySafeDefaults()
    {
        Group = string.IsNullOrWhiteSpace(Group) ? null : Group;
        MouseProfileSettings.ValidateRange(Mode, 0, 1, nameof(Mode));
        if (FunctionData is { Count: > 5 })
        {
            throw new InvalidDataException("Function data must contain at most 5 bytes.");
        }
        MouseProfileSettings.ValidateRange(OptionIndex, 0, byte.MaxValue, nameof(OptionIndex));
        MouseProfileSettings.ValidateRange(ModifierIndex, 0, 15, nameof(ModifierIndex));
        ValidateFiniteRange(TurboRate, 1, 50, nameof(TurboRate));
        ValidateFiniteRange(ProfileId, 1, 5, nameof(ProfileId));
        ValidateFiniteRange(DpiClutchX, 100, 30_000, nameof(DpiClutchX));
        ValidateFiniteRange(DpiClutchY, 100, 30_000, nameof(DpiClutchY));
        MouseProfileSettings.ValidateRange(MacroPlaybackIndex, 0, 4, nameof(MacroPlaybackIndex));
        MouseProfileSettings.ValidateRange(MacroTargetIndex, 0, 1, nameof(MacroTargetIndex));
        // 0..3 keep their original order; 4 is HoldRepeat.
        MouseProfileSettings.ValidateRange(
            SoftwareTriggerModeIndex,
            0,
            4,
            nameof(SoftwareTriggerModeIndex));
        ValidateFiniteRange(MacroRepeatCount, 1, byte.MaxValue, nameof(MacroRepeatCount));
        MouseProfileSettings.ValidateRange(
            SelectedMacroSlotIndex,
            0,
            byte.MaxValue,
            nameof(SelectedMacroSlotIndex));
        Extra = Extra is null ? null : ProfileDictionary.Normalize(Extra);
    }

    public ButtonMappingProfile Clone()
    {
        ApplySafeDefaults();
        return new()
        {
            ButtonId = ButtonId,
            Mode = Mode,
            Group = Group,
            FunctionId = FunctionId,
            FunctionData = FunctionData?.ToList(),
            OptionIndex = OptionIndex,
            ModifierIndex = ModifierIndex,
            TurboRate = TurboRate,
            ProfileId = ProfileId,
            DpiClutchX = DpiClutchX,
            DpiClutchY = DpiClutchY,
            MacroPlaybackIndex = MacroPlaybackIndex,
            MacroTargetIndex = MacroTargetIndex,
            SoftwareTriggerModeIndex = SoftwareTriggerModeIndex,
            MacroRepeatCount = MacroRepeatCount,
            SelectedMacroSlotIndex = SelectedMacroSlotIndex,
            Extra = Extra is null
                ? null
                : new Dictionary<string, string>(Extra, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static void ValidateFiniteRange(double? value, double minimum, double maximum, string name)
    {
        if (value is { } finite && (!double.IsFinite(finite) || finite < minimum || finite > maximum))
        {
            throw new InvalidDataException($"{name} must be between {minimum} and {maximum}.");
        }
    }
}
