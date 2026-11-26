using System.Text.Json;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Splits a macro into a shared implementation and per-profile bindings.
///
/// Steps, name, playback and mouse tracking live in <see cref="ProfileDocument.MacroLibrary"/>.
/// Every profile keeps an entry with the same id that adds its own trigger keys,
/// device assignment and enabled switch. A new profile therefore starts with
/// every macro available, unbound, instead of an empty library.
/// </summary>
public static class MacroLibrary
{
    public const int MaxMacros = 64;

    /// <summary>
    /// After reading the file: the active profile wins an id conflict because it
    /// is what the user was just editing. A profile whose copy differs gets a new
    /// id instead of being overwritten, so neither macro is lost.
    /// </summary>
    public static void NormalizeOnLoad(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureProfileCatalog();
        document.MacroLibrary ??= [];

        var active = document.GetActiveProfileDefinition();
        foreach (var macro in active.Macros)
        {
            Upsert(document, macro);
        }

        foreach (var profile in OrderedProfiles(document, skipActive: true))
        {
            foreach (var macro in profile.Macros)
            {
                var existing = Find(document.MacroLibrary, macro.Id);
                if (existing is null)
                {
                    document.MacroLibrary.Add(ClonePayload(macro, macro.Id));
                    continue;
                }

                if (SamePayload(existing, macro))
                {
                    continue;
                }

                // Same id, different steps: an older per-profile setup. Keep both.
                macro.Id = Guid.NewGuid().ToString("N");
                document.MacroLibrary.Add(ClonePayload(macro, macro.Id));
            }
        }

        Rebuild(document);
    }

    /// <summary>
    /// Before writing the file: only the active profile can have been edited, so
    /// it refreshes the library and the other profiles are brought in line.
    /// </summary>
    public static void NormalizeOnSave(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureProfileCatalog();
        document.MacroLibrary ??= [];

        var active = document.GetActiveProfileDefinition();
        foreach (var macro in active.Macros)
        {
            Upsert(document, macro);
        }

        foreach (var profile in OrderedProfiles(document, skipActive: true))
        {
            foreach (var macro in profile.Macros)
            {
                if (Find(document.MacroLibrary, macro.Id) is null)
                {
                    // A profile that never reached the library (older file or a
                    // half-finished import) still keeps its macro.
                    document.MacroLibrary.Add(ClonePayload(macro, macro.Id));
                }
            }
        }

        Rebuild(document);
    }

    /// <summary>
    /// Gives a new profile an unbound entry for every library macro, so its
    /// macro list and the device dropdown are populated before the next save.
    /// </summary>
    internal static void Seed(ProfileDocument document, ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);
        document.MacroLibrary ??= [];
        foreach (var payload in document.MacroLibrary)
        {
            if (payload is null ||
                profile.Macros.Any(macro =>
                    StringComparer.Ordinal.Equals(macro.Id, payload.Id)))
            {
                continue;
            }

            profile.Macros.Add(ClonePayload(payload, payload.Id));
        }
    }

    /// <summary>Profile names that bind this macro, ignoring <paramref name="exceptProfileName"/>.</summary>
    public static IReadOnlyList<string> ProfilesBinding(
        ProfileDocument document,
        string macroId,
        string? exceptProfileName = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureProfileCatalog();
        return document.Profiles
            .Where(pair =>
                !StringComparer.OrdinalIgnoreCase.Equals(pair.Key, exceptProfileName) &&
                pair.Value.Macros.Any(macro =>
                    StringComparer.Ordinal.Equals(macro.Id, macroId) &&
                    macro.AllTriggers.Any()))
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Deletes the macro from the library and from every profile.</summary>
    public static void Remove(ProfileDocument document, string macroId)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.MacroLibrary?.RemoveAll(macro =>
            StringComparer.Ordinal.Equals(macro.Id, macroId));
        foreach (var profile in document.Profiles.Values)
        {
            profile.Macros.RemoveAll(macro =>
                StringComparer.Ordinal.Equals(macro.Id, macroId));
        }
    }

    private static void Rebuild(ProfileDocument document)
    {
        var library = (document.MacroLibrary ?? [])
            .Where(macro => macro is not null)
            .GroupBy(macro => macro.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(MaxMacros)
            .ToList();

        // An entry no profile references is a leftover (rolled-back import, or a
        // macro removed through an older code path). Drop it instead of
        // resurrecting it into every profile.
        var referenced = document.Profiles.Values
            .SelectMany(profile => profile.Macros)
            .Select(macro => macro.Id)
            .ToHashSet(StringComparer.Ordinal);
        library.RemoveAll(macro => !referenced.Contains(macro.Id));
        document.MacroLibrary = library;

        var order = library
            .Select((macro, index) => (macro.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        foreach (var profile in document.Profiles.Values)
        {
            profile.Macros.RemoveAll(macro =>
                macro is null || !order.ContainsKey(macro.Id));
            foreach (var payload in library)
            {
                var entry = profile.Macros.FirstOrDefault(macro =>
                    StringComparer.Ordinal.Equals(macro.Id, payload.Id));
                if (entry is null)
                {
                    // Enabled, triggers and the device assignment start empty in
                    // a profile that never bound this macro.
                    profile.Macros.Add(ClonePayload(payload, payload.Id));
                }
                else
                {
                    CopyPayload(payload, entry);
                }
            }

            profile.Macros.Sort((left, right) => order[left.Id].CompareTo(order[right.Id]));
        }
    }

    private static void Upsert(ProfileDocument document, MacroDefinition macro)
    {
        if (Find(document.MacroLibrary, macro.Id) is { } existing)
        {
            CopyPayload(macro, existing);
            return;
        }

        document.MacroLibrary.Add(ClonePayload(macro, macro.Id));
    }

    private static MacroDefinition? Find(IEnumerable<MacroDefinition> macros, string id) =>
        macros.FirstOrDefault(macro =>
            macro is not null && StringComparer.Ordinal.Equals(macro.Id, id));

    private static IEnumerable<ProfileDefinition> OrderedProfiles(
        ProfileDocument document,
        bool skipActive)
    {
        foreach (var (name, profile) in document.Profiles
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (skipActive &&
                StringComparer.OrdinalIgnoreCase.Equals(name, document.ActiveProfileName))
            {
                continue;
            }

            if (profile is not null)
            {
                yield return profile;
            }
        }
    }

    private static MacroDefinition ClonePayload(MacroDefinition source, string id)
    {
        var payload = new MacroDefinition { Id = id };
        CopyPayload(source, payload);
        return payload;
    }

    private static void CopyPayload(MacroDefinition source, MacroDefinition target)
    {
        target.Name = source.Name;
        target.DefaultTriggerMode = source.DefaultTriggerMode;
        target.PlaybackMode = source.PlaybackMode;
        target.RepeatCount = source.RepeatCount;
        target.PlaybackSpeed = source.PlaybackSpeed;
        target.MouseTrackingMode = source.MouseTrackingMode;
        target.Steps = source.Steps.Select(step => step.Clone()).ToList();
    }

    private static bool SamePayload(MacroDefinition left, MacroDefinition right) =>
        StringComparer.Ordinal.Equals(left.Name, right.Name) &&
        left.DefaultTriggerMode == right.DefaultTriggerMode &&
        left.PlaybackMode == right.PlaybackMode &&
        left.RepeatCount == right.RepeatCount &&
        Math.Abs(left.PlaybackSpeed - right.PlaybackSpeed) < 0.0001 &&
        left.MouseTrackingMode == right.MouseTrackingMode &&
        StringComparer.Ordinal.Equals(
            JsonSerializer.Serialize(left.Steps),
            JsonSerializer.Serialize(right.Steps));
}
