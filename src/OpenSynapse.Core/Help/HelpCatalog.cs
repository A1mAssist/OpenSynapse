namespace OpenSynapse.Core.Help;

public static class HelpCatalog
{
    public static IReadOnlyList<HelpCategory> Categories { get; } =
    [
        Category("getting-started"),
        Category("devices"),
        Category("lighting"),
        Category("macros"),
        Category("profiles"),
        Category("diagnostics"),
        Category("troubleshooting"),
    ];

    public static IReadOnlyList<HelpCommonTask> CommonTasks { get; } =
    [
        new("getting-started.connect-device", "Help_Common_ConnectDevice"),
        new("lighting.browse-scenes", "Help_Common_ApplyLighting"),
        new("macros.record-edit", "Help_Common_CreateMacro"),
        new("troubleshooting.device-not-recognized", "Help_Common_DeviceNotRecognized"),
    ];

    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        Topic(
            "getting-started.first-launch", "getting-started", HelpAudience.General,
            P("getting-started.first-launch"), P("getting-started.first-launch", "Before"),
            Steps("getting-started.first-launch",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "settings")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("getting-started.first-launch", "Check"),
            Callout(HelpCalloutKind.Tip, "getting-started.first-launch"),
            Faq("getting-started.first-launch", 3),
            Related("getting-started.connect-device", "getting-started.settings", "profiles.create-backup")),
        Topic(
            "getting-started.connect-device", "getting-started", HelpAudience.Devices,
            P("getting-started.connect-device"), P("getting-started.connect-device", "Before"),
            Steps("getting-started.connect-device",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("getting-started.connect-device", "Check"),
            Callout(HelpCalloutKind.Caution, "getting-started.connect-device"),
            Faq("getting-started.connect-device", 3),
            Related("devices.overview", "troubleshooting.device-not-recognized", "devices.hue-pairing")),
        Topic(
            "getting-started.settings", "getting-started", HelpAudience.General,
            P("getting-started.settings"), P("getting-started.settings", "Before"),
            Steps("getting-started.settings",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "settings")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("getting-started.settings", "Check"),
            Callout(HelpCalloutKind.Tip, "getting-started.settings"),
            Faq("getting-started.settings", 3),
            Related("getting-started.first-launch", "devices.mouse-power", "diagnostics.logs")),
        Topic(
            "devices.overview", "devices", HelpAudience.Devices,
            P("devices.overview"), P("devices.overview", "Before"),
            Steps("devices.overview",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.overview", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.overview"),
            Faq("devices.overview", 3),
            Related("devices.keyboard", "devices.mouse", "devices.kiyo-camera", "devices.cooling-pad")),
        Topic(
            "devices.keyboard", "devices", HelpAudience.Keyboard,
            P("devices.keyboard"), P("devices.keyboard", "Before"),
            Steps("devices.keyboard",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.keyboard", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.keyboard"),
            Faq("devices.keyboard", 3),
            Related("devices.keyboard-mapping", "devices.keyboard-actuation", "macros.trigger-binding", "profiles.device-rules")),
        Topic(
            "devices.mouse", "devices", HelpAudience.Mouse,
            P("devices.mouse"), P("devices.mouse", "Before"),
            Steps("devices.mouse",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            new HelpSpecimenBlock(HelpSpecimenKind.DpiStages),
            P("devices.mouse", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.mouse"),
            Faq("devices.mouse", 3),
            Related("devices.mouse-performance", "devices.mouse-power", "devices.keyboard-mapping", "macros.trigger-binding")),
        Topic(
            "devices.blade", "devices", HelpAudience.Blade,
            P("devices.blade"), P("devices.blade", "Before"),
            Steps("devices.blade",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.blade", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.blade"),
            Faq("devices.blade", 3),
            Related("devices.blade-fan-gpu", "devices.headset-thx", "lighting.browse-scenes", "profiles.create-backup")),
        Topic(
            "devices.headset", "devices", HelpAudience.Headset,
            P("devices.headset"), P("devices.headset", "Before"),
            Steps("devices.headset",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.headset", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.headset"),
            Faq("devices.headset", 3),
            Related("devices.headset-thx", "devices.microphone", "devices.speaker")),
        Topic(
            "devices.speaker", "devices", HelpAudience.Speaker,
            P("devices.speaker"), P("devices.speaker", "Before"),
            Steps("devices.speaker",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.speaker", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.speaker"),
            Faq("devices.speaker", 3),
            Related("devices.headset-thx", "lighting.browse-scenes", "diagnostics.logs")),
        Topic(
            "devices.microphone", "devices", HelpAudience.Microphone,
            P("devices.microphone"), P("devices.microphone", "Before"),
            Steps("devices.microphone",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.microphone", "Check"),
            Callout(HelpCalloutKind.Tip, "devices.microphone"),
            Faq("devices.microphone", 3),
            Related("devices.headset", "devices.speaker", "diagnostics.logs")),
        Topic(
            "devices.mousepad", "devices", HelpAudience.Mousepad,
            P("devices.mousepad"), P("devices.mousepad", "Before"),
            Steps("devices.mousepad",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.mousepad", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.mousepad"),
            Faq("devices.mousepad", 3),
            Related("devices.mouse", "lighting.apply-scope", "lighting.stop-restore")),
        Topic(
            "devices.monitor", "devices", HelpAudience.Monitor,
            P("devices.monitor"), P("devices.monitor", "Before"),
            Steps("devices.monitor",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.monitor", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.monitor"),
            Faq("devices.monitor", 3),
            Related("lighting.browse-scenes", "lighting.apply-scope", "devices.blade")),
        Topic(
            "devices.controller", "devices", HelpAudience.Controller,
            P("devices.controller"), P("devices.controller", "Before"),
            Steps("devices.controller",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.controller", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.controller"),
            Faq("devices.controller", 3),
            Related("devices.keyboard-mapping", "profiles.create-backup", "diagnostics.logs")),
        Topic(
            "devices.accessory", "devices", HelpAudience.Accessory,
            P("devices.accessory"), P("devices.accessory", "Before"),
            Steps("devices.accessory",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.accessory", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.accessory"),
            Faq("devices.accessory", 3),
            Related("devices.cooling-pad", "devices.kiyo-camera", "devices.network-lighting", "lighting.apply-scope")),
        Topic(
            "devices.network-lighting", "devices", HelpAudience.NetworkLighting,
            P("devices.network-lighting"), P("devices.network-lighting", "Before"),
            Steps("devices.network-lighting",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "settings")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.network-lighting", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.network-lighting"),
            Faq("devices.network-lighting", 3),
            Related("devices.hue-pairing", "devices.accessory", "diagnostics.logs")),
        Topic(
            "devices.keyboard-mapping", "devices", HelpAudience.Keyboard,
            P("devices.keyboard-mapping"), P("devices.keyboard-mapping", "Before"),
            Steps("devices.keyboard-mapping",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.keyboard-mapping", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.keyboard-mapping"),
            Faq("devices.keyboard-mapping", 3),
            Related("devices.keyboard", "macros.trigger-binding", "macros.playback-loops")),
        Topic(
            "devices.keyboard-actuation", "devices", HelpAudience.Keyboard,
            P("devices.keyboard-actuation"), P("devices.keyboard-actuation", "Before"),
            Steps("devices.keyboard-actuation",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("devices.keyboard-actuation", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.keyboard-actuation"),
            Faq("devices.keyboard-actuation", 3),
            Related("devices.keyboard", "devices.keyboard-mapping", "profiles.device-rules")),
        Topic(
            "devices.mouse-performance", "devices", HelpAudience.Mouse,
            P("devices.mouse-performance"), P("devices.mouse-performance", "Before"),
            Steps("devices.mouse-performance",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("devices.mouse-performance", "Check"),
            Callout(HelpCalloutKind.Tip, "devices.mouse-performance"),
            Faq("devices.mouse-performance", 3),
            Related("devices.mouse", "devices.mouse-power", "profiles.device-rules")),
        Topic(
            "devices.mouse-power", "devices", HelpAudience.Mouse,
            P("devices.mouse-power"), P("devices.mouse-power", "Before"),
            Steps("devices.mouse-power",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.mouse-power", "Check"),
            Callout(HelpCalloutKind.Tip, "devices.mouse-power"),
            Faq("devices.mouse-power", 3),
            Related("devices.mouse-performance", "getting-started.settings", "troubleshooting.device-not-recognized")),
        Topic(
            "devices.blade-fan-gpu", "devices", HelpAudience.Blade,
            P("devices.blade-fan-gpu"), P("devices.blade-fan-gpu", "Before"),
            Steps("devices.blade-fan-gpu",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("devices.blade-fan-gpu", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.blade-fan-gpu"),
            Faq("devices.blade-fan-gpu", 3),
            Related("devices.blade", "profiles.create-backup", "diagnostics.logs")),
        Topic(
            "devices.headset-thx", "devices", HelpAudience.Headset,
            P("devices.headset-thx"), P("devices.headset-thx", "Before"),
            Steps("devices.headset-thx",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("devices.headset-thx", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.headset-thx"),
            Faq("devices.headset-thx", 3),
            Related("devices.headset", "devices.blade", "diagnostics.logs")),
        Topic(
            "devices.kiyo-camera", "devices", HelpAudience.Accessory,
            P("devices.kiyo-camera"), P("devices.kiyo-camera", "Before"),
            Steps("devices.kiyo-camera",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("devices.kiyo-camera", "Check"),
            Callout(HelpCalloutKind.Limit, "devices.kiyo-camera"),
            Faq("devices.kiyo-camera", 3),
            Related("devices.accessory", "getting-started.connect-device", "diagnostics.logs")),
        Topic(
            "devices.cooling-pad", "devices", HelpAudience.Accessory,
            P("devices.cooling-pad"), P("devices.cooling-pad", "Before"),
            Steps("devices.cooling-pad",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("devices.cooling-pad", "Check"),
            Callout(HelpCalloutKind.Caution, "devices.cooling-pad"),
            Faq("devices.cooling-pad", 3),
            Related("devices.blade-fan-gpu", "devices.accessory", "profiles.create-backup")),
        Topic(
            "devices.hue-pairing", "devices", HelpAudience.NetworkLighting,
            P("devices.hue-pairing"), P("devices.hue-pairing", "Before"),
            Steps("devices.hue-pairing",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "settings")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("devices.hue-pairing", "Check"),
            Callout(HelpCalloutKind.Tip, "devices.hue-pairing"),
            Faq("devices.hue-pairing", 3),
            Related("devices.network-lighting", "getting-started.connect-device", "diagnostics.logs")),
        Topic(
            "lighting.browse-scenes", "lighting", HelpAudience.Studio,
            P("lighting.browse-scenes"), P("lighting.browse-scenes", "Before"),
            Steps("lighting.browse-scenes",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "studio")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            new HelpSpecimenBlock(HelpSpecimenKind.SceneCard),
            P("lighting.browse-scenes", "Check"),
            Callout(HelpCalloutKind.Caution, "lighting.browse-scenes"),
            Faq("lighting.browse-scenes", 3),
            Related("lighting.apply-scope", "lighting.layers", "lighting.stop-restore")),
        Topic(
            "lighting.apply-scope", "lighting", HelpAudience.Studio,
            P("lighting.apply-scope"), P("lighting.apply-scope", "Before"),
            Steps("lighting.apply-scope",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "studio")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            new HelpSpecimenBlock(HelpSpecimenKind.ApplyScope),
            P("lighting.apply-scope", "Check"),
            Callout(HelpCalloutKind.Caution, "lighting.apply-scope"),
            Faq("lighting.apply-scope", 3),
            Related("lighting.browse-scenes", "lighting.layers", "lighting.stop-restore")),
        Topic(
            "lighting.stop-restore", "lighting", HelpAudience.Studio,
            P("lighting.stop-restore"), P("lighting.stop-restore", "Before"),
            Steps("lighting.stop-restore",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "studio")), ("Step2", null),
                ("Step3", null), ("Step4", null)),
            P("lighting.stop-restore", "Check"),
            Callout(HelpCalloutKind.Limit, "lighting.stop-restore"),
            Faq("lighting.stop-restore", 3),
            Related("lighting.apply-scope", "troubleshooting.lighting-not-restored", "getting-started.settings")),
        Topic(
            "lighting.layers", "lighting", HelpAudience.Studio,
            P("lighting.layers"), P("lighting.layers", "Before"),
            Steps("lighting.layers",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "studio")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null), ("Step6", null)),
            P("lighting.layers", "Check"),
            Callout(HelpCalloutKind.Tip, "lighting.layers"),
            Faq("lighting.layers", 3),
            Related("lighting.browse-scenes", "lighting.apply-scope", "lighting.screen-sampling")),
        Topic(
            "lighting.screen-sampling", "lighting", HelpAudience.Studio,
            P("lighting.screen-sampling"), P("lighting.screen-sampling", "Before"),
            Steps("lighting.screen-sampling",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "studio")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null), ("Step6", null)),
            P("lighting.screen-sampling", "Check"),
            Callout(HelpCalloutKind.Caution, "lighting.screen-sampling"),
            Faq("lighting.screen-sampling", 3),
            Related("lighting.layers", "lighting.apply-scope", "lighting.stop-restore")),
        Topic(
            "macros.record-edit", "macros", HelpAudience.Macros,
            P("macros.record-edit"), P("macros.record-edit", "Before"),
            Steps("macros.record-edit",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "macros")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            new HelpSpecimenBlock(HelpSpecimenKind.MacroStep),
            P("macros.record-edit", "Check"),
            Callout(HelpCalloutKind.Tip, "macros.record-edit"),
            Faq("macros.record-edit", 3),
            Related("macros.trigger-binding", "macros.playback-loops", "macros.import-export")),
        Topic(
            "macros.trigger-binding", "macros", HelpAudience.Macros,
            P("macros.trigger-binding"), P("macros.trigger-binding", "Before"),
            Steps("macros.trigger-binding",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "macros")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            new HelpSpecimenBlock(HelpSpecimenKind.KeyBinding),
            P("macros.trigger-binding", "Check"),
            Callout(HelpCalloutKind.Caution, "macros.trigger-binding"),
            Faq("macros.trigger-binding", 3),
            Related("macros.record-edit", "devices.keyboard-mapping", "macros.playback-loops")),
        Topic(
            "macros.playback-loops", "macros", HelpAudience.Macros,
            P("macros.playback-loops"), P("macros.playback-loops", "Before"),
            Steps("macros.playback-loops",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "macros")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("macros.playback-loops", "Check"),
            Callout(HelpCalloutKind.Caution, "macros.playback-loops"),
            Faq("macros.playback-loops", 3),
            Related("macros.record-edit", "macros.trigger-binding", "devices.keyboard-mapping")),
        Topic(
            "macros.import-export", "macros", HelpAudience.Macros,
            P("macros.import-export"), P("macros.import-export", "Before"),
            Steps("macros.import-export",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "macros")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("macros.import-export", "Check"),
            Callout(HelpCalloutKind.Tip, "macros.import-export"),
            Faq("macros.import-export", 3),
            Related("macros.record-edit", "macros.playback-loops", "profiles.create-backup")),
        Topic(
            "profiles.device-rules", "profiles", HelpAudience.Profiles,
            P("profiles.device-rules"), P("profiles.device-rules", "Before"),
            Steps("profiles.device-rules",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "profiles")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("profiles.device-rules", "Check"),
            Callout(HelpCalloutKind.Caution, "profiles.device-rules"),
            Faq("profiles.device-rules", 3),
            Related("profiles.create-backup", "devices.keyboard", "devices.mouse")),
        Topic(
            "profiles.create-backup", "profiles", HelpAudience.Profiles,
            P("profiles.create-backup"), P("profiles.create-backup", "Before"),
            Steps("profiles.create-backup",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "profiles")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null), ("Step6", null)),
            P("profiles.create-backup", "Check"),
            Callout(HelpCalloutKind.Caution, "profiles.create-backup"),
            Faq("profiles.create-backup", 3),
            Related("profiles.device-rules", "macros.import-export", "lighting.layers")),
        Topic(
            "diagnostics.logs", "diagnostics", HelpAudience.Diagnostics,
            P("diagnostics.logs"), P("diagnostics.logs", "Before"),
            Steps("diagnostics.logs",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "diagnostics")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("diagnostics.logs", "Check"),
            Callout(HelpCalloutKind.Tip, "diagnostics.logs"),
            Faq("diagnostics.logs", 3),
            Related("troubleshooting.device-not-recognized", "troubleshooting.lighting-not-restored", "devices.headset-thx")),
        Topic(
            "troubleshooting.device-not-recognized", "troubleshooting", HelpAudience.Troubleshooting,
            P("troubleshooting.device-not-recognized"), P("troubleshooting.device-not-recognized", "Before"),
            Steps("troubleshooting.device-not-recognized",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "devices")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("troubleshooting.device-not-recognized", "Check"),
            Callout(HelpCalloutKind.Limit, "troubleshooting.device-not-recognized"),
            Faq("troubleshooting.device-not-recognized", 3),
            Related("getting-started.connect-device", "devices.overview", "diagnostics.logs")),
        Topic(
            "troubleshooting.lighting-not-restored", "troubleshooting", HelpAudience.Troubleshooting,
            P("troubleshooting.lighting-not-restored"), P("troubleshooting.lighting-not-restored", "Before"),
            Steps("troubleshooting.lighting-not-restored",
                ("Step1", new HelpAction(HelpActionKind.NavigatePage, "studio")), ("Step2", null),
                ("Step3", null), ("Step4", null),
                ("Step5", null)),
            P("troubleshooting.lighting-not-restored", "Check"),
            Callout(HelpCalloutKind.Caution, "troubleshooting.lighting-not-restored"),
            Faq("troubleshooting.lighting-not-restored", 3),
            Related("lighting.stop-restore", "lighting.apply-scope", "diagnostics.logs")),
    ];

    public static IReadOnlyDictionary<string, HelpCategory> CategoriesById { get; } =
        Categories.ToDictionary(category => category.Id, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, HelpTopic> TopicsById { get; } =
        Topics.ToDictionary(topic => topic.Id, StringComparer.Ordinal);

    public static IReadOnlyList<HelpTopic> TopicsInCategory(string categoryId) =>
        Topics.Where(topic => StringComparer.Ordinal.Equals(topic.CategoryId, categoryId)).ToArray();

    public static HelpTopic? Previous(string topicId)
    {
        var index = IndexOf(topicId);
        return index > 0 ? Topics[index - 1] : null;
    }

    public static HelpTopic? Next(string topicId)
    {
        var index = IndexOf(topicId);
        return index >= 0 && index + 1 < Topics.Count ? Topics[index + 1] : null;
    }

    public static string TopicKey(string topicId, string suffix) =>
        $"Help_{NormalizeId(topicId)}_{suffix}";

    public static string CategoryKey(string categoryId, string suffix) =>
        $"Help_Category_{NormalizeId(categoryId)}_{suffix}";

    public static string AudienceKey(HelpAudience audience) =>
        $"Help_Audience_{audience}";

    private static int IndexOf(string topicId)
    {
        for (var index = 0; index < Topics.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(Topics[index].Id, topicId))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeId(string id) => id.Replace('.', '_').Replace('-', '_');

    private static HelpCategory Category(string id) =>
        new(id, CategoryKey(id, "Title"), CategoryKey(id, "Description"));

    private static HelpTopic Topic(
        string id,
        string categoryId,
        HelpAudience audience,
        params HelpBlock[] blocks) =>
        new(
            id,
            categoryId,
            TopicKey(id, "Title"),
            TopicKey(id, "Summary"),
            TopicKey(id, "Keywords"),
            audience,
            blocks);

    private static HelpParagraphBlock P(string topicId, string suffix = "Intro") =>
        new(TopicKey(topicId, suffix));

    private static HelpStepsBlock Steps(
        string topicId,
        params (string Suffix, HelpAction? Action)[] steps) =>
        new(steps.Select(step => new HelpStep(TopicKey(topicId, step.Suffix), step.Action)).ToArray());

    private static HelpCalloutBlock Callout(HelpCalloutKind kind, string topicId) =>
        new(kind, TopicKey(topicId, "Note1"));

    private static HelpFaqBlock Faq(string topicId, int count = 1) =>
        new(Enumerable.Range(1, count).Select(index =>
            new HelpFaqItem(TopicKey(topicId, $"Faq{index}Q"), TopicKey(topicId, $"Faq{index}A"))).ToArray());

    private static HelpRelatedBlock Related(params string[] topicIds) => new(topicIds);
}
