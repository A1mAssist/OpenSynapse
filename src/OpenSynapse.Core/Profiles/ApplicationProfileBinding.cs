namespace OpenSynapse.Core.Profiles;

public static class ApplicationProfileBinding
{
    public static void Bind(ProfileDocument document, string executablePath, string profileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = NormalizePath(executablePath);
        document.EnsureProfileCatalog();
        var profile = document.Profiles.Keys.FirstOrDefault(name =>
            StringComparer.OrdinalIgnoreCase.Equals(name, profileName?.Trim()));
        if (profile is null)
        {
            throw new KeyNotFoundException($"Profile '{profileName}' does not exist.");
        }

        document.ApplicationBindings[path] = profile;
    }

    public static bool Unbind(ProfileDocument document, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ApplicationBindings.Remove(NormalizePath(executablePath));
    }

    public static string? Resolve(ProfileDocument document, string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var path = NormalizePath(executablePath);
        if (!document.ApplicationBindings.TryGetValue(path, out var profileName))
        {
            var match = document.ApplicationBindings.FirstOrDefault(binding =>
                StringComparer.OrdinalIgnoreCase.Equals(binding.Key, path));
            profileName = match.Value;
        }

        return profileName is not null && document.Profiles.ContainsKey(profileName)
            ? profileName
            : null;
    }

    private static string NormalizePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return Path.GetFullPath(executablePath.Trim());
    }
}

public sealed class ApplicationProfileSwitcher
{
    private string? _fallbackProfileName;

    public ApplicationProfileSwitcher Clone() => new()
    {
        _fallbackProfileName = _fallbackProfileName,
    };

    public bool Update(ProfileDocument document, string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureProfileCatalog();
        var boundProfile = ApplicationProfileBinding.Resolve(document, executablePath);
        if (boundProfile is not null)
        {
            _fallbackProfileName ??= document.ActiveProfileName;
            if (StringComparer.OrdinalIgnoreCase.Equals(document.ActiveProfileName, boundProfile))
            {
                return false;
            }

            ProfileCatalog.Select(document, boundProfile);
            return true;
        }

        if (_fallbackProfileName is null)
        {
            return false;
        }

        var fallback = document.Profiles.ContainsKey(_fallbackProfileName)
            ? _fallbackProfileName
            : document.Profiles.Keys.First();
        _fallbackProfileName = null;
        if (StringComparer.OrdinalIgnoreCase.Equals(document.ActiveProfileName, fallback))
        {
            return false;
        }

        ProfileCatalog.Select(document, fallback);
        return true;
    }
}
