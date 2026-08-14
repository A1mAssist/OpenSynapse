namespace OpenSynapse.Core.Profiles;

public static class ProfileCatalog
{
    public const string DefaultProfileName = "Default";

    public static IReadOnlyList<string> GetNames(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureProfileCatalog();
        return document.Profiles.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void Select(ProfileDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeName(name);
        document.EnsureProfileCatalog();
        if (!document.Profiles.ContainsKey(normalized))
        {
            throw new KeyNotFoundException($"Profile '{normalized}' does not exist.");
        }

        document.ActiveProfileName = normalized;
        document.EnsureProfileCatalog();
    }

    public static void Create(ProfileDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeName(name);
        document.EnsureProfileCatalog();
        EnsureUnique(document, normalized);
        document.Profiles.Add(normalized, new ProfileDefinition());
    }

    public static void Clone(ProfileDocument document, string sourceName, string destinationName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var source = NormalizeName(sourceName);
        var destination = NormalizeName(destinationName);
        document.EnsureProfileCatalog();
        if (!document.Profiles.TryGetValue(source, out var sourceProfile))
        {
            throw new KeyNotFoundException($"Profile '{source}' does not exist.");
        }

        EnsureUnique(document, destination);
        document.Profiles.Add(destination, sourceProfile.Clone());
    }

    public static void Rename(ProfileDocument document, string currentName, string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var current = NormalizeName(currentName);
        var replacement = NormalizeName(newName);
        document.EnsureProfileCatalog();
        if (!document.Profiles.TryGetValue(current, out var profile))
        {
            throw new KeyNotFoundException($"Profile '{current}' does not exist.");
        }
        if (!StringComparer.OrdinalIgnoreCase.Equals(current, replacement))
        {
            EnsureUnique(document, replacement);
            document.Profiles.Remove(current);
            document.Profiles[replacement] = profile;
            foreach (var path in document.ApplicationBindings
                         .Where(binding => StringComparer.OrdinalIgnoreCase.Equals(binding.Value, current))
                         .Select(binding => binding.Key)
                         .ToArray())
            {
                document.ApplicationBindings[path] = replacement;
            }
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(document.ActiveProfileName, current))
        {
            document.ActiveProfileName = replacement;
            document.EnsureProfileCatalog();
        }
    }

    public static void Delete(ProfileDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeName(name);
        document.EnsureProfileCatalog();
        if (document.Profiles.Count == 1)
        {
            throw new InvalidOperationException("The last profile cannot be deleted.");
        }
        if (!document.Profiles.Remove(normalized))
        {
            throw new KeyNotFoundException($"Profile '{normalized}' does not exist.");
        }
        foreach (var path in document.ApplicationBindings
                     .Where(binding => StringComparer.OrdinalIgnoreCase.Equals(binding.Value, normalized))
                     .Select(binding => binding.Key)
                     .ToArray())
        {
            document.ApplicationBindings.Remove(path);
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(document.ActiveProfileName, normalized))
        {
            document.ActiveProfileName = document.Profiles.Keys.First();
            document.EnsureProfileCatalog();
        }
    }

    private static void EnsureUnique(ProfileDocument document, string name)
    {
        if (document.Profiles.ContainsKey(name))
        {
            throw new InvalidOperationException($"Profile '{name}' already exists.");
        }
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 64 || normalized.Any(char.IsControl) ||
            normalized.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
        {
            throw new ArgumentException("Profile name contains unsupported characters or is too long.", nameof(name));
        }

        return normalized;
    }
}
