using OpenSynapse.Core.Audio;

namespace OpenSynapse.App;

internal static class ThxPresetDisplayNames
{
    public static string Get(ThxAudioPreset preset)
    {
        // A separate vendor/user label takes precedence over our names for native IDs.
        if (!string.IsNullOrWhiteSpace(preset.Name) &&
            !string.Equals(preset.Name, preset.Id, StringComparison.OrdinalIgnoreCase))
            return preset.Name;

        var key = preset.Id switch
        {
            "game" => "ThxPresetGame",
            "movie" => "ThxPresetMovie",
            "music" => "ThxPresetMusic",
            "voice" => "ThxPresetVoice",
            "custom" or "Custom" => "ThxPresetCustom",
            "default" => "ThxPresetDefault",
            "flat" => "ThxPresetFlat",
            "podcast" => "ThxPresetPodcast",
            "Acoustic" => "ThxPresetAcoustic",
            "Cinema Mode" => "ThxPresetCinemaMode",
            "Classical" => "ThxPresetClassical",
            "EDM" => "ThxPresetEdm",
            "Flat / Live" => "ThxPresetFlatLive",
            "Game Grand" => "ThxPresetGameGrand",
            "Game Hyperlocator Mode" => "ThxPresetGameHyperlocator",
            "Game Mode" => "ThxPresetGameMode",
            "Hip Hop" => "ThxPresetHipHop",
            "Jazz" => "ThxPresetJazz",
            "Music Mode" => "ThxPresetMusicMode",
            "Pop" => "ThxPresetPop",
            "R&B" => "ThxPresetRhythmBlues",
            "Rock" => "ThxPresetRock",
            "Voice Mode" => "ThxPresetVoiceMode",
            _ => null,
        };
        if (key is not null) return AppStrings.Text(key);

        // Exact IDs from the installed THX USB 1532/0529 catalog. Do not interpret
        // suffixes on unknown/custom names or infer a different game's sequel number.
        var game = preset.Id switch
        {
            "AnomalyPresident_imm" => "Anomaly President",
            "BigWalk_imm" => "Big Walk",
            "CallofDuty_ModernWarfare4_imm" or "CallofDuty_ModernWarfare4_cmm" => "Call of Duty: Modern Warfare 4",
            "EndOfAbyss_imm" => "End of Abyss",
            "GuildWars2_imm" => "Guild Wars 2",
            "HellLetLooseVietnam_imm" or "HellLetLooseVietnam_cmm" => "Hell Let Loose: Vietnam",
            "IronNestHeavyTurretSimulator_imm" => "Iron Nest: Heavy Turret Simulator",
            "MARVELTokonFightingSouls_imm" => "MARVEL Tōkon: Fighting Souls",
            "MortalShell2_imm" => "Mortal Shell 2",
            "NoMoreRoomInHell2_imm" => "No More Room in Hell 2",
            "STALKER2CostOfHope_imm" => "S.T.A.L.K.E.R. 2: Cost of Hope",
            "STARWARSZeroCompany_imm" => "STAR WARS Zero Company",
            _ => null,
        };
        return game is null ? preset.DisplayName : AppStrings.FormatText(
            preset.Id.EndsWith("_cmm", StringComparison.Ordinal)
                ? "ThxPresetCompetitiveFormat" : "ThxPresetImmersiveFormat", game);
    }
}
