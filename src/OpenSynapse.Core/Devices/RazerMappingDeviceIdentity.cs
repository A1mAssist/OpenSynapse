using System.Text;
using System.Text.RegularExpressions;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Compares the product name a mapping bundle declares with the catalog name. Both sides
/// use different suffixes for the same model ("Wired", "35K", "SE"), so only known
/// non-model qualifiers are accepted; a different family or model word is a mismatch.
/// </summary>
public static class RazerMappingDeviceIdentity
{
    private static readonly string[] VariantTokens =
    [
        "readonlyprobe", "xbox25anniversaryedition", "xboxoneedition",
        "carbonfiberedition", "phantomgreenedition", "anniversaryedition",
        "classicedition", "fakeredition", "dvaedition", "hyperpolling",
        "hyperspeed", "wireless", "receiver", "bluetooth", "alternate",
        "carbonfiber", "phantomgreen", "edition", "chroma", "classic",
        "faker", "dongle", "jp", "wired", "8khz", "35k", "24ghz", "usb",
        "3500", "2000", "1800", "se",
        "white", "black", "quartz", "mercury", "red", "green", "pink", "yellow",
    ];

    // Both sides abbreviate the same edition; expand on the raw name so the words are
    // still separable.
    private static readonly (string Short, string Long)[] Abbreviations =
    [
        ("TE", "Tournament Edition"),
        ("TKL", "Tenkeyless"),
    ];

    public static bool Matches(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return true;
        }

        var actualName = Normalize(actual);
        if (actualName.Length == 0)
        {
            return true;
        }

        foreach (var candidate in expected.Split(
            '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expectedName = Normalize(candidate);
            if (expectedName.Length == 0)
            {
                continue;
            }

            if (string.Equals(expectedName, actualName, StringComparison.Ordinal))
            {
                return true;
            }

            if (expectedName.StartsWith(actualName, StringComparison.Ordinal) &&
                IsVariantSuffix(expectedName[actualName.Length..]))
            {
                return true;
            }

            if (actualName.StartsWith(expectedName, StringComparison.Ordinal) &&
                IsVariantSuffix(actualName[expectedName.Length..]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string name)
    {
        foreach (var (shortForm, longForm) in Abbreviations)
        {
            name = Regex.Replace(name, $@"\b{shortForm}\b", longForm, RegexOptions.IgnoreCase);
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        // Generations differ between the page and the catalog (V4 Mini page still says
        // V3 Mini); the model words are what decide identity.
        var normalized = Regex.Replace(builder.ToString(), "v[0-9]+", "v");
        return normalized.StartsWith("razer", StringComparison.Ordinal)
            ? normalized[5..]
            : normalized;
    }

    private static bool IsVariantSuffix(string suffix)
    {
        while (suffix.Length > 0)
        {
            var token = VariantTokens.FirstOrDefault(candidate =>
                suffix.StartsWith(candidate, StringComparison.Ordinal));
            if (token is not null)
            {
                suffix = suffix[token.Length..];
                continue;
            }

            var digits = 0;
            while (digits < suffix.Length && char.IsAsciiDigit(suffix[digits]))
            {
                digits++;
            }

            if (digits > 0)
            {
                suffix = suffix[digits..];
                continue;
            }

            return false;
        }

        return true;
    }
}
