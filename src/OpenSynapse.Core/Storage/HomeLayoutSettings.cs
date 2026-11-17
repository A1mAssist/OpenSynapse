using System.Text.Json;

namespace OpenSynapse.Core.Storage;

public sealed record HomeCardSetting(string CardId, bool Visible, int Order);
public enum HomeQuickActionKind { OpenDevice, OpenStudio, SelectDeviceProfile, ToggleApplicationSwitching }
public sealed record HomeQuickAction(string Id, HomeQuickActionKind Kind,
    OpenSynapse.Core.Profiles.DeviceBindingKey? Device, string? ProfileName);
public sealed record HomeLayoutDocument(int Version, IReadOnlyList<HomeCardSetting> Cards,
    IReadOnlyList<HomeQuickAction> Actions);

public sealed class HomeLayoutSettings
{
    public const string OpenDevice = "OpenDevice";
    public const string OpenStudio = "OpenStudio";
    private static readonly HashSet<string> AllowedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "devices", "quick-actions", "profiles", "telemetry",
    };

    public List<string> CardOrder { get; set; } = ["telemetry", "devices", "status", "quick-actions", "profiles"];
    public HashSet<string> HiddenCards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HomeQuickAction> Actions { get; set; } = [];

    public HomeLayoutDocument ToDocument() => new(1,
        CardOrder.Select((id, index) => new HomeCardSetting(id, !HiddenCards.Contains(id), index)).ToArray(), Actions);

    public static HomeLayoutSettings FromDocument(HomeLayoutDocument? document)
    {
        if (document?.Cards is null || document.Version != 1) return CreateDefault();
        return Sanitize(new HomeLayoutSettings
        {
            CardOrder = document.Cards.OrderBy(card => card.Order).Select(card => card.CardId).ToList(),
            HiddenCards = document.Cards.Where(card => !card.Visible).Select(card => card.CardId).ToHashSet(),
            Actions = document.Actions?.Where(action => Enum.IsDefined(action.Kind)).ToList() ?? [],
        });
    }

    public static HomeLayoutSettings CreateDefault() => new();

    public static HomeLayoutSettings Sanitize(HomeLayoutSettings? settings)
    {
        if (settings is null)
        {
            return CreateDefault();
        }

        var order = new List<string>();
        foreach (var id in settings.CardOrder ?? [])
        {
            if (TryGetKnownId(id, out var known) && !order.Contains(known, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(known);
            }
        }

        foreach (var id in AllowedIds)
        {
            if (!order.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(id);
            }
        }

        return new HomeLayoutSettings
        {
            CardOrder = order,
            HiddenCards = new HashSet<string>(
                (settings.HiddenCards ?? []).Select(id => TryGetKnownId(id, out var known) ? known : null)
                    .OfType<string>(),
                StringComparer.OrdinalIgnoreCase),
            Actions = settings.Actions ?? [],
        };
    }

    public static HomeLayoutSettings Load(string json)
    {
        try
        {
            return Sanitize(JsonSerializer.Deserialize<HomeLayoutSettings>(json));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return CreateDefault();
        }
    }

    public static bool IsQuickAction(string id) =>
        id is OpenStudio;

    private static bool TryGetKnownId(string? id, out string known)
    {
        known = id?.Trim().ToLowerInvariant() ?? string.Empty;
        return AllowedIds.Contains(known);
    }
}
