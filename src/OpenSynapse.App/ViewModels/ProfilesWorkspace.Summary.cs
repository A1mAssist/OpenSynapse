using System.Collections.ObjectModel;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed record ProfileSummaryGroupViewModel(string Title, string Summary);

public sealed partial class ProfilesWorkspace
{
    private bool _hasProfileSummary;

    public ObservableCollection<ProfileSummaryGroupViewModel> ProfileSummaryGroups { get; } = new();
    public bool HasProfileSummary => _hasProfileSummary;
    public bool IsProfileSummaryEmpty => !_hasProfileSummary;

    /// <summary>Rebuilds the read-only description of the active profile.</summary>
    internal void RefreshProfileSummary()
    {
        ProfileSummaryGroups.Clear();
        foreach (var group in ProfileSummaryBuilder.Build(_getProfile(), _getActiveProfile()))
        {
            var items = group.Items.Select(FormatSummaryItem).ToArray();
            if (items.Length > 0)
            {
                var separator = StringComparer.Ordinal.Equals(
                    group.TitleKey,
                    "ProfileSummary_Group_Lighting")
                    ? Environment.NewLine
                    : " · ";
                ProfileSummaryGroups.Add(new ProfileSummaryGroupViewModel(
                    AppStrings.Text(group.TitleKey),
                    string.Join(separator, items)));
            }
        }

        _hasProfileSummary = ProfileSummaryGroups.Count > 0;
        OnPropertyChanged(nameof(HasProfileSummary));
        OnPropertyChanged(nameof(IsProfileSummaryEmpty));
    }

    private static string FormatSummaryItem(ProfileSummaryItem item)
    {
        var label = item.Arguments.Count == 0
            ? AppStrings.Text(item.LabelKey)
            : AppStrings.FormatText(item.LabelKey, item.Arguments.ToArray());
        var scope = item.Scope switch
        {
            ProfileSummaryScope.Device => AppStrings.Text("ProfileSummary_ScopeDevice"),
            ProfileSummaryScope.PluggedIn => AppStrings.Text("BladeLightingPowerPluggedIn"),
            ProfileSummaryScope.LowPowerAdapter => AppStrings.Text("BladeLightingPowerLowAdapter"),
            ProfileSummaryScope.OnBattery => AppStrings.Text("BladeLightingPowerBattery"),
            _ => null,
        };
        return scope is null ? label : $"{label} · {scope}";
    }
}
