#if DEBUG
using OpenSynapse.Core.Audio;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ThxAudioViewModel
{
    internal static string VerifyPresetLocalization()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("THX localization QA requires an isolated instance.");

        string[] ids =
        [
            "Acoustic", "AnomalyPresident_imm", "BigWalk_imm", "CallofDuty_ModernWarfare4_cmm",
            "CallofDuty_ModernWarfare4_imm", "Cinema Mode", "Classical", "Custom", "EDM",
            "EndOfAbyss_imm", "Flat / Live", "Game Grand", "Game Hyperlocator Mode", "Game Mode",
            "GuildWars2_imm", "HellLetLooseVietnam_cmm", "HellLetLooseVietnam_imm", "Hip Hop",
            "IronNestHeavyTurretSimulator_imm", "Jazz", "MARVELTokonFightingSouls_imm", "MortalShell2_imm",
            "Music Mode", "NoMoreRoomInHell2_imm", "Pop", "R&B", "Rock", "STALKER2CostOfHope_imm",
            "STARWARSZeroCompany_imm", "Voice Mode",
            "game", "movie", "music", "voice", "custom", "default", "flat", "podcast",
        ];
        var presets = ids.Select(id => new ThxAudioPreset(id, id)).Concat(
            [new("my_imm", "my_imm"), new("Custom", "My personal EQ"), new("user-id", "Cinema Mode")]).ToArray();
        var state = new ThxAudioState(ThxAudioEndpoint.Headphones, true, true,
            "CallofDuty_ModernWarfare4_cmm", presets, [], new Dictionary<string, IReadOnlyList<double>>(), null, null, true);
        var headset = new RazerHeadsetAudioViewModel(new RazerHeadsetAudioConnection(
            "qa-thx-localization", 0x0529, "Razer USB Sound Card", 91, HasControlInterface: false));
        var blade = new ThxAudioViewModel(null, true);
        var language = AppLanguageSettings.Current;
        try
        {
            foreach (var (locale, gameMode, immersive, competitive) in new[]
            {
                ("en-US", "Game Mode", "Immersive Mode", "Competitive Mode"),
                ("zh-CN", "游戏模式", "沉浸模式", "竞技模式"),
                ("zh-TW", "遊戲模式", "沉浸模式", "競技模式"),
                ("de-DE", "Spielmodus", "Immersiver Modus", "Wettbewerbsmodus"),
                ("fr-FR", "Mode jeu", "Mode immersif", "Mode compétitif"),
                ("ja-JP", "ゲームモード", "没入モード", "競技モード"),
                ("ko-KR", "게임 모드", "몰입 모드", "경쟁 모드"),
                ("ru-RU", "Игровой режим", "Режим погружения", "Соревновательный режим"),
            })
            {
                AppLanguageSettings.Save(locale);
                AppStrings.Reset();
                string Name(string id) => ThxPresetDisplayNames.Get(new(id, id));
                if (Name("Game Mode") != gameMode ||
                    Name("EndOfAbyss_imm") != "End of Abyss · " + immersive ||
                    Name("CallofDuty_ModernWarfare4_cmm") != "Call of Duty: Modern Warfare 4 · " + competitive)
                    throw new InvalidOperationException("THX resource lookup failed for " + locale);

                // Neither preview has a controller/Accessories attachment; applying readback cannot write hardware.
                headset.SetAutoApplyPreviewState(
                    new RazerHeadsetAudioState(null, null, null, null, null, null, null, null, null, null, null, null, []), state);
                blade.ApplyStatus(new(ThxAudioAvailability.Available, null, state));
                var labels = headset.ThxPresetOptions;
                if (!labels.SequenceEqual(blade.Presets.Select(preset => preset.Label)) ||
                    !presets.Select(preset => preset.Id).SequenceEqual(blade.Presets.Select(preset => preset.Id)) ||
                    headset.SelectedThxPresetIndex != 3 || blade.SelectedPresetIndex != 3 ||
                    blade._state?.SelectedPresetId != state.SelectedPresetId ||
                    labels.Take(ids.Length).Any(label => label.StartsWith("ThxPreset", StringComparison.Ordinal) || label.Contains('_')) ||
                    !labels.Skip(ids.Length).SequenceEqual(["my_imm", "My personal EQ", "Cinema Mode"]))
                    throw new InvalidOperationException("THX display/ID separation failed for " + locale);
            }
        }
        finally
        {
            AppLanguageSettings.Save(language);
            AppStrings.Reset();
        }
        return "PASS THX localization: 8 languages, 30 native + 8 standard IDs, both pages, raw IDs and custom names";
    }
}
#endif
