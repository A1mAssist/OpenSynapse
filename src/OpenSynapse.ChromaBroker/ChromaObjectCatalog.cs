// Chroma shared-memory object/layout reference:
// worflor/neuron crates/neuron-host/src/adapters/chroma_shm.rs (GPL-3.0-or-later).
// This C# port is part of OpenSynapse and distributed under AGPL-3.0.
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaBroker;

internal enum ChromaBrokerDeviceClass : byte
{
    Keyboard = 1,
    Mouse = 2,
    Headset = 3,
    Mousepad = 4,
    Keypad = 5,
    ChromaLink = 6,
}

internal static class ChromaBrokerDeviceClasses
{
    public static readonly ChromaBrokerDeviceClass[] All =
    [
        ChromaBrokerDeviceClass.Keyboard,
        ChromaBrokerDeviceClass.Mouse,
        ChromaBrokerDeviceClass.Headset,
        ChromaBrokerDeviceClass.Mousepad,
        ChromaBrokerDeviceClass.Keypad,
        ChromaBrokerDeviceClass.ChromaLink,
    ];

    public static byte Bit(this ChromaBrokerDeviceClass device) => device switch
    {
        ChromaBrokerDeviceClass.Keyboard => 0x01,
        ChromaBrokerDeviceClass.Mouse => 0x02,
        ChromaBrokerDeviceClass.Headset => 0x04,
        ChromaBrokerDeviceClass.Mousepad => 0x08,
        ChromaBrokerDeviceClass.Keypad => 0x10,
        ChromaBrokerDeviceClass.ChromaLink => 0x80,
        _ => 0,
    };

    public static ChromaSdkDeviceType ToSdkType(this ChromaBrokerDeviceClass device) => device switch
    {
        ChromaBrokerDeviceClass.Keyboard => ChromaSdkDeviceType.Keyboard,
        ChromaBrokerDeviceClass.Mouse => ChromaSdkDeviceType.Mouse,
        ChromaBrokerDeviceClass.Headset => ChromaSdkDeviceType.Headset,
        ChromaBrokerDeviceClass.Mousepad => ChromaSdkDeviceType.Mousepad,
        ChromaBrokerDeviceClass.Keypad => ChromaSdkDeviceType.Keypad,
        ChromaBrokerDeviceClass.ChromaLink => ChromaSdkDeviceType.ChromaLink,
        _ => throw new ArgumentOutOfRangeException(nameof(device)),
    };

    public static string SectionGuid(this ChromaBrokerDeviceClass device) => device switch
    {
        ChromaBrokerDeviceClass.Keyboard => "74164FAD-E73C-4FA1-A9AA-70813315ED9C",
        ChromaBrokerDeviceClass.Mouse => "0DBE78AC-AC93-408F-A27E-8F61EA067B05",
        ChromaBrokerDeviceClass.Headset => "CDB274E2-C50A-4425-8076-1E71550CBE8A",
        ChromaBrokerDeviceClass.Mousepad => "17EFA16B-E476-4E43-A98A-3AA837681741",
        ChromaBrokerDeviceClass.Keypad => "0FFE5A62-387E-4360-95A3-5D8D4075780D",
        ChromaBrokerDeviceClass.ChromaLink => "8AE08F8C-BE3E-4248-AB01-0B595960EC3E",
        _ => throw new ArgumentOutOfRangeException(nameof(device)),
    };
}

internal enum ChromaObjectKind
{
    Section,
    Event,
    Mutex,
    Unknown,
}

internal enum ChromaObjectOrigin
{
    ServerCreated,
    ClientCreated,
    OnDemand,
}

internal sealed record ChromaObjectDefinition(
    string Guid,
    ChromaObjectKind Kind,
    ChromaObjectOrigin Origin,
    int Size = 0);

internal static class ChromaObjectCatalog
{
    public const string ControlSection = "96264859-7FDE-4DA1-A433-BB5109D17DB2";
    public const string AppRegistry = "D4E1A960-872F-4BF8-B09A-9E54F646D7CE";
    public const string SessionInfo = "821AA2A2-8215-4A16-BE9D-7CD8CEBDC398";
    public const string SessionWorkerEvent = "B8B918C0-9790-47F2-AC7A-F36B8414140C";
    public const string ServerOnlineEvent = "A84AF9C8-EFE0-430D-871C-10DA760C2CCD";
    public const string MaskPerUserMutex = "63D31EEC-008F-43C9-A58E-ED6949B25A6C";

    public static readonly string[] MaskMutexes =
    [
        "B1570C3F-8B14-45B0-BCEB-C57ED1F5C589",
        "3DD569A2-BC96-425D-ABEF-A5EF21F4B681",
        "08B4F43A-DA51-4120-B388-CE0F8CE6F61A",
    ];

    public static readonly string[] MaskEvents =
    [
        "86E8A3B0-C718-4997-AF5D-C3677E71F5D8",
        "9138EDDD-890B-46E2-8B64-E0037E2B332D",
        "798D9FEC-789F-46FD-B3D9-359C8E81DD11",
        "841EB9A8-6DA7-479E-94DC-C23B95FFDF43",
    ];

    // Full 35-object capture. ServerCreated is created here; ClientCreated is
    // opened if present; OnDemand is owned by the game/session lifecycle.
    public static readonly ChromaObjectDefinition[] Objects =
    [
        new("60C824F3-B985-436A-BF6E-8FEBC076B2DA", ChromaObjectKind.Event, ChromaObjectOrigin.ServerCreated),
        new("CB3C8DAE-6F34-4A92-8657-191C8FFDB156", ChromaObjectKind.Event, ChromaObjectOrigin.ServerCreated),
        new("0DB0CEFA-C51E-4255-87FB-2D36A0159896", ChromaObjectKind.Event, ChromaObjectOrigin.ServerCreated),
        new("45C97C2C-2D50-4F30-B50E-AFBB1CE22E93", ChromaObjectKind.Event, ChromaObjectOrigin.ServerCreated),
        new("4D006319-9569-4E38-B0DF-811AA2DF115F", ChromaObjectKind.Event, ChromaObjectOrigin.ServerCreated),
        new("DA5A60F0-A3C5-4335-A039-BCC6136C61A3", ChromaObjectKind.Event, ChromaObjectOrigin.ServerCreated),
        new("153ABAD2-474D-4DDF-A39A-A4DE0E66D1C0", ChromaObjectKind.Mutex, ChromaObjectOrigin.ServerCreated),
        new("A114B7A2-5A4C-4DCD-9507-0E3FED86AC35", ChromaObjectKind.Mutex, ChromaObjectOrigin.ServerCreated),
        new(ControlSection, ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 130804),
        new("735A64EB-02D2-498D-954E-23FC11A050A9", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 1637048),
        new("74164FAD-E73C-4FA1-A9AA-70813315ED9C", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 40088),
        new("E26206E3-F6C2-4E07-BA5C-6C214FD726D9", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 38808),
        new(AppRegistry, ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 26932),
        new("0DBE78AC-AC93-408F-A27E-8F61EA067B05", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 14888),
        new("9B7B099A-F7F3-44CE-AB88-28F79D6D273A", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 14488),
        new("8AE08F8C-BE3E-4248-AB01-0B595960EC3E", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 12808),
        new("17EFA16B-E476-4E43-A98A-3AA837681741", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 12328),
        new("0FFE5A62-387E-4360-95A3-5D8D4075780D", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 11848),
        new("CDB274E2-C50A-4425-8076-1E71550CBE8A", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 11048),
        new("D42A3EF5-1B8D-4055-B42F-C5D34A9CA4E4", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 10568),
        new("D41D8537-2D95-4AD2-8A77-51DC00946366", ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 168),
        new(SessionInfo, ChromaObjectKind.Section, ChromaObjectOrigin.ServerCreated, 84),
        new("0F9297E6-E80C-47E4-9A8B-1237E50484B7", ChromaObjectKind.Event, ChromaObjectOrigin.ClientCreated),
        new("89811F96-91C2-4C19-8E0A-54469F491550", ChromaObjectKind.Event, ChromaObjectOrigin.ClientCreated),
        new(ServerOnlineEvent, ChromaObjectKind.Event, ChromaObjectOrigin.ClientCreated),
        new("5CD8AF82-56E4-4C36-9144-6D04931A522B", ChromaObjectKind.Mutex, ChromaObjectOrigin.ClientCreated),
        new("1C68F494-B74D-46E5-9A2F-56F8C526A7C9", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("416FA77A-EC97-44AA-9C3C-DFEA2AC245D3", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("5C49A446-0B97-46CA-BD60-EE5CAF8DDD59", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("6D0C47C9-E199-48C4-B55D-5298974EF8F3", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("893ED63D-F9D7-472A-AA34-FFB18CF28C55", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("9FE422BE-A752-4F67-9EC6-11ED6135478E", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("A966C3C0-231A-4BE5-9C90-5E0C80349891", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new(SessionWorkerEvent, ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
        new("FFED75C2-17DC-4886-AA2C-DBAF1F662351", ChromaObjectKind.Unknown, ChromaObjectOrigin.OnDemand),
    ];

    public static string Name(string guid) => $@"Global\{{{guid}}}";

    public static string PerUserMaskName(string userName) => Name(MaskPerUserMutex) + userName;
}
