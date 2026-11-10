using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenSynapse.Core.Devices;

public static partial class RazerMappingBundleParser
{
    private static readonly Regex ButtonListScopeRegex = new(
        """buttonList\s*:\s*\[""",
        RegexOptions.CultureInvariant);

    private static readonly Regex GroupListScopeRegex = new(
        """groupList\s*=\s*\[""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ExportedButtonListRegex = new(
        """BUTTON_LIST\s*:\s*\(\s*\)\s*=>\s*(?<name>[A-Za-z_$][\w$]*)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitButtonIdRegex = new(
        """\{(?=[^{}]*?(?:inputID|buttonKey):")[^{}]*?\bkey:\s*(?<number>\d+)[^{}]*\}""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ButtonKeyRegex = new(
        """buttonKey:\s*"(?<text>[^"]*)""",
        RegexOptions.CultureInvariant);

    private static Dictionary<string, byte> ParseExplicitButtonIds(string text)
    {
        var buttonIds = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (Match match in ExplicitButtonIdRegex.Matches(text))
        {
            if (!byte.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var key) || key == 0)
            {
                continue;
            }

            AddKey(buttonIds, MatchText(match.Value, ButtonKeyRegex), key);
            AddKey(buttonIds, MatchText(match.Value, InputIdRegex), key);
        }

        return buttonIds;
    }

    private static (string Scope, bool IncludeInputIdOnly) FindButtonListScope(string text)
    {
        var match = ButtonListScopeRegex.Match(text);
        if (!match.Success)
        {
            match = GroupListScopeRegex.Match(text);
        }

        if (!match.Success)
        {
            return FindExportedButtonListScope(text);
        }

        var open = match.Index + match.Length - 1;
        var close = FindMatchingBracket(text, open);
        return close > open ? (text[open..(close + 1)], false) : (text, false);
    }

    /// <summary>
    /// 0098-style bundles export BUTTON_LIST from a plain inputID array instead of
    /// passing it to drawLines. Follow the export alias so unrelated DKM objects do
    /// not become fallback mapping rows.
    /// </summary>
    private static (string Scope, bool IncludeInputIdOnly) FindExportedButtonListScope(string text)
    {
        var export = ExportedButtonListRegex.Match(text);
        if (!export.Success)
        {
            return (text, false);
        }

        var name = export.Groups["name"].Value;
        var assignment = new Regex(
            $"""(?<![\w$]){Regex.Escape(name)}\s*=\s*\[""",
            RegexOptions.CultureInvariant).Match(text, export.Index);
        if (!assignment.Success)
        {
            return (text, false);
        }

        var open = assignment.Index + assignment.Length - 1;
        var close = FindMatchingBracket(text, open);
        return close > open ? (text[open..(close + 1)], true) : (text, false);
    }

    private static int FindMatchingBracket(string text, int open)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var i = open; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'' or '`')
            {
                quote = ch;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
