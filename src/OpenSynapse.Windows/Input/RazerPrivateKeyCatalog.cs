namespace OpenSynapse.Windows.Input;

/// <summary>
/// Maps a Razer product's private DKM key slot to the label Synapse uses.
/// Slot numbers are product-scoped: 32 is DPI+ on a mouse and M1 on a keyboard.
/// </summary>
internal static class RazerPrivateKeyCatalog
{
    private static readonly Dictionary<ushort, IReadOnlyDictionary<int, string>> Mappings = Build(
        "0080,0083,008A,008C,00A1,00A2,00A3,00A5,00B8,00BE,00C0,00C4,00DE,00DF,00E5,00E6,00EF,00F0|82=CycleUpSensitivityStages",
        "0226,0228,0243,024E,025A,025D,0266,026B,026C,0292,0295,0298,02A5|82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "0073,0075,007C,0084,00AF,0A22|32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile",
        "02A6,02CF,02D0,02D8,02E5,02E6|100=LEFT_BUTTON_02;101=LEFT_BUTTON_03;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "028F,0294,02A1,02A2,02A3|80=MEDIA_VOLUME_DOWN;81=MEDIA_VOLUME_UP;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "0078,007A,0091,0094,00E0|34=Button4;35=Button5;82=CycleUpSensitivityStages",
        "0099,00AA,00AB,00CB,00CC,00D6|80=CycleUpProfile;81=SensitivityClutch",
        "009E,00B2,00B4,00B6,00C2|32=SensitivityStageUp;33=SensitivityStageDown;82=CycleUpSensitivityStages",
        "0067,008D,008F,0093|2=Key1;3=Key2;32=SensitivityStageUp;33=SensitivityStageDown;4=Key3;5=Key4;6=Key5;7=Key6;8=Key7;80=CycleUpProfile",
        "0063,006A,006B|32=SensitivityStageUp;33=SensitivityStageDown;34=Button5;35=Button4",
        "0059,0070|32=SensitivityStageUp;33=SensitivityStageDown;34=Button4;35=Button5;80=CycleUpProfile",
        "0046,009C|32=SensitivityStageUp;33=SensitivityStageDown",
        "00A4,00CF|210=Key9;32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile;82=CycleUpSensitivityStages;84=ScrollModeSwitch;87=CycleUpScrollStages;89=Key8",
        "0203,0221|32=M1;33=M2;34=M3;35=M4;36=M5",
        "0287,0293|32=M1;33=M2;34=M3;35=M4;36=M5;37=M6;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "00E2,00E3,00EB|80=CycleUpProfile",
        "0085,0086|32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile;81=SensitivityClutch",
        "00C7,00D0|210=AI_Launcher;82=CycleUpSensitivityStages",
        "0096|2=Key1;3=Key2;4=Key3;5=Key4;6=Key5;7=Key6;8=Key7;80=CycleUpProfile;82=CycleUpSensitivityStages",
        "0065|32=CycleUpSensitivityStages;81=SensitivityClutch",
        "0064|32=SensitivityStageUp;33=SensitivityStageDown;35=Button4;80=CycleUpProfile;81=SensitivityClutch",
        "006C|32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile;82=CycleUpSensitivityStages",
        "02CC|246=BUTTON_D2;247=AI_Launcher;32=BUTTON_M_01;33=BUTTON_M_02;34=BUTTON_M_03;35=BUTTON_M_04;65=BUTTON_D41;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "00DA|210=AI_Launcher;32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile;82=CycleUpSensitivityStages",
        "00E7,00E8|2=Key1;210=Key9;3=Key2;32=SensitivityStageUp;33=SensitivityStageDown;4=Key3;5=Key4;6=Key5;7=Key6;8=Key7;80=CycleUpProfile;84=ScrollModeSwitch;89=Key8",
        "02EB|1=KEY_FN;103=DKM_KEYPAD_103;104=DKM_KEYPAD_104;105=KEY_ID_RIGHT_STICK_BTN",
        "02C4|247=AI_Launcher;32=BUTTON_M_01;33=BUTTON_M_02;34=BUTTON_M_03;35=BUTTON_M_04;36=BUTTON_M_05;65=BUTTON_D41;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY;96=DKM_KB_LEFTKNOB",
        "00DD|33=SensitivityStageDown;80=CycleUpProfile",
        "00D3|210=AI_Launcher;82=CycleUpSensitivityStages;89=Razer Hypershift",
        "0050|2=Key1;3=Key2;32=SensitivityStageUp;33=SensitivityStageDown;4=Key3;5=Key4;6=Key5;7=Key6;8=Key7",
        "02D4|246=BUTTON_D2;247=AI_Launcher;32=BUTTON_M_01;33=BUTTON_M_02;34=BUTTON_M_03;35=BUTTON_M_04;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "00F1|210=Key9;32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile;82=CycleUpSensitivityStages;84=ScrollModeSwitch;87=CycleUpScrollStages",
        "02B3|103=DKM_KB_LEFTKNOB;82=DIAL_CLICK",
        "00B9|82=CycleUpSensitivityStages;89=Razer Hypershift",
        "025E|80=MEDIA_VOLUME_DOWN;81=MEDIA_VOLUME_UP;82=MEDIA_MUTE;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "02A7|100=LEFT_BUTTON_02;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "00A7,00A8|2=Key1;3=Key2;32=SensitivityStageUp;33=SensitivityStageDown;4=Key3;5=Key4;6=Key5;7=Key6;8=Key7;80=CycleUpProfile;82=CycleUpSensitivityStages;87=CycleUpScrollStages",
        "02D7|246=BUTTON_D2;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "02F4|82=DIAL_CLICK",
        "028D|100=LEFT_BUTTON_02;101=LEFT_BUTTON_03;32=BUTTON_M_01;33=BUTTON_M_02;34=BUTTON_M_03;35=BUTTON_M_04;36=BUTTON_M_05;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY;96=DKM_KB_LEFTKNOB;99=LEFT_BUTTON_01",
        "00B3|210=Key9;32=SensitivityStageUp;33=SensitivityStageDown;80=CycleUpProfile;82=CycleUpSensitivityStages;84=ScrollModeSwitch;89=Key8",
        "0062|32=CycleUpSensitivityStages",
        "02F0|246=DKM_KB_XBOX;82=DIAL_CLICK;83=MEDIA_NEXT;84=MEDIA_PREV;85=MEDIA_PAUSE_PLAY",
        "02C6|3=Game Mode;10=HyperShift;11=HyperShift;210=AI Launcher;211=Performance;212=Mic Mute");

    internal static string Describe(ushort productId, int keyCode)
    {
        if (Mappings.TryGetValue(productId, out var mapping) &&
            mapping.TryGetValue(keyCode, out var label))
        {
            return Friendly(label);
        }

        return $"Razer key {keyCode}";
    }

    private static string Friendly(string label) => label switch
    {
        "SensitivityStageUp" => "DPI +",
        "SensitivityStageDown" => "DPI -",
        "SensitivityClutch" => "DPI Clutch",
        "CycleUpSensitivityStages" => "Next DPI Stage",
        "CycleUpScrollStages" => "Next Scroll Mode",
        "CycleUpProfile" => "Next Profile",
        "ScrollModeSwitch" => "Scroll Mode",
        "DIAL_CLICK" => "Dial Click",
        "MEDIA_VOLUME_DOWN" => "Volume -",
        "MEDIA_VOLUME_UP" => "Volume +",
        "MEDIA_MUTE" => "Mute",
        "MEDIA_NEXT" => "Next Track",
        "MEDIA_PREV" => "Previous Track",
        "MEDIA_PAUSE_PLAY" => "Play/Pause",
        "AI_Launcher" => "AI Launcher",
        "Razer Hypershift" => "HyperShift",
        "LEFT_BUTTON_01" => "Side Key 1",
        "LEFT_BUTTON_02" => "Side Key 2",
        "LEFT_BUTTON_03" => "Side Key 3",
        _ => label.Replace('_', ' '),
    };

    private static Dictionary<ushort, IReadOnlyDictionary<int, string>> Build(params string[] groups)
    {
        var result = new Dictionary<ushort, IReadOnlyDictionary<int, string>>();
        foreach (var group in groups)
        {
            var parts = group.Split('|', 2);
            var mapping = parts[1]
                .Split(';')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    pair => int.Parse(pair[0], System.Globalization.CultureInfo.InvariantCulture),
                    pair => pair[1]);
            foreach (var pid in parts[0].Split(','))
            {
                result[Convert.ToUInt16(pid, 16)] = mapping;
            }
        }

        return result;
    }
}
