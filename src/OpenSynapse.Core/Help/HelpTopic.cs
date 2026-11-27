namespace OpenSynapse.Core.Help;

public enum HelpAudience
{
    General,
    Devices,
    Keyboard,
    Mouse,
    Blade,
    Headset,
    Speaker,
    Microphone,
    Mousepad,
    Monitor,
    Controller,
    Accessory,
    NetworkLighting,
    Studio,
    Macros,
    Profiles,
    Diagnostics,
    Troubleshooting,
}

public enum HelpBlockKind
{
    Paragraph,
    Steps,
    Specimen,
    Callout,
    Faq,
    Related,
}

public enum HelpCalloutKind
{
    Tip,
    Caution,
    Limit,
}

public enum HelpSpecimenKind
{
    DpiStages,
    SceneCard,
    ApplyScope,
    MacroStep,
    KeyBinding,
}

public enum HelpActionKind
{
    NavigatePage,
}

public sealed record HelpCategory(string Id, string TitleKey, string DescriptionKey);

public sealed record HelpCommonTask(string TopicId, string TitleKey);

public sealed record HelpTopic(
    string Id,
    string CategoryId,
    string TitleKey,
    string SummaryKey,
    string KeywordsKey,
    HelpAudience Audience,
    IReadOnlyList<HelpBlock> Blocks);

public abstract record HelpBlock(HelpBlockKind Kind);

public sealed record HelpParagraphBlock(string TextKey)
    : HelpBlock(HelpBlockKind.Paragraph);

public sealed record HelpStepsBlock(IReadOnlyList<HelpStep> Steps)
    : HelpBlock(HelpBlockKind.Steps);

public sealed record HelpSpecimenBlock(HelpSpecimenKind Specimen)
    : HelpBlock(HelpBlockKind.Specimen);

public sealed record HelpCalloutBlock(HelpCalloutKind Callout, string TextKey)
    : HelpBlock(HelpBlockKind.Callout);

public sealed record HelpFaqBlock(IReadOnlyList<HelpFaqItem> Items)
    : HelpBlock(HelpBlockKind.Faq);

public sealed record HelpRelatedBlock(IReadOnlyList<string> TopicIds)
    : HelpBlock(HelpBlockKind.Related);

public sealed record HelpStep(string TextKey, HelpAction? Action = null)
{
    public bool HasAction => Action is not null;
}

public sealed record HelpAction(HelpActionKind Kind, string Target);

public sealed record HelpFaqItem(string QuestionKey, string AnswerKey);
