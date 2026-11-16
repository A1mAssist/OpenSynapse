using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenSynapse.Core.Devices;

public static partial class RazerMappingBundleParser
{
    private sealed record DrawCall(
        int Index,
        int MatchOffset,
        int ButtonIndex,
        double X,
        double Y,
        bool IsLeft,
        int BlockId);

    private static List<List<ButtonObject>> ParseButtonScopes(string text)
    {
        var scopes = new List<List<ButtonObject>>();
        var seen = new HashSet<int>();

        void Add(string scope, bool includeInputIdOnly, int open)
        {
            if (!seen.Add(open))
            {
                return;
            }

            var buttons = ParseButtonObjects(scope, includeInputIdOnly);
            if (buttons.Count > 0)
            {
                scopes.Add(buttons);
            }
        }

        foreach (Match match in ButtonListScopeRegex.Matches(text))
        {
            var open = match.Index + match.Length - 1;
            var close = FindMatchingDelimiter(text, open, '[', ']');
            if (close > open)
            {
                Add(text[open..(close + 1)], false, open);
            }
        }

        if (scopes.Count == 0)
        {
            foreach (Match match in GroupListScopeRegex.Matches(text))
            {
                var open = match.Index + match.Length - 1;
                var close = FindMatchingDelimiter(text, open, '[', ']');
                if (close > open)
                {
                    Add(text[open..(close + 1)], false, open);
                }
            }
        }

        if (scopes.Count == 0)
        {
            var (scope, includeInputIdOnly) = FindExportedButtonListScope(text);
            Add(scope, includeInputIdOnly, -1);
        }

        return scopes;
    }

    private static IReadOnlyList<ButtonObject> SelectScope(
        IReadOnlyList<DrawCall> calls,
        IReadOnlyList<List<ButtonObject>> scopes)
    {
        if (scopes.Count == 0)
        {
            return [];
        }

        var maxIndex = calls.Count == 0 ? -1 : calls.Max(call => call.ButtonIndex);
        return scopes
            .Where(scope => scope.Count > maxIndex)
            .OrderBy(scope => scope.Count)
            .FirstOrDefault() ?? scopes[0];
    }

    private static List<DrawCall> ParseDrawCalls(string text, IReadOnlySet<string> buttonListAliases)
    {
        var calls = new List<DrawCall>();
        var blocks = new List<(int Start, int End)>();
        foreach (Match match in DrawBlockRegex.Matches(text))
        {
            var open = match.Index + match.Length - 1;
            var close = FindMatchingDelimiter(text, open, '{', '}');
            if (close > open)
            {
                blocks.Add((open, close));
            }
        }

        for (var blockId = 0; blockId < blocks.Count; blockId++)
        {
            var (start, end) = blocks[blockId];
            var blockText = text[start..(end + 1)];
            foreach (Match match in DrawLineRegex.Matches(blockText))
            {
                AddCall(match, start, blockId, false, buttonListAliases, calls);
            }

            foreach (Match match in DrawSideLineRegex.Matches(blockText))
            {
                AddCall(match, start, blockId, true, buttonListAliases, calls);
            }
        }

        foreach (Match match in DrawLineRegex.Matches(text))
        {
            if (!Inside(match.Index, blocks))
            {
                AddCall(match, 0, -1, false, buttonListAliases, calls);
            }
        }

        foreach (Match match in DrawSideLineRegex.Matches(text))
        {
            if (!Inside(match.Index, blocks))
            {
                AddCall(match, 0, -1, true, buttonListAliases, calls);
            }
        }

        return calls;

        static bool Inside(int index, List<(int Start, int End)> ranges) =>
            ranges.Any(range => index >= range.Start && index <= range.End);
    }

    private static void AddCall(
        Match match,
        int offset,
        int blockId,
        bool sideLine,
        IReadOnlySet<string> buttonListAliases,
        List<DrawCall> calls)
    {
        var buttonGroup = match.Groups["button"];
        if (!buttonGroup.Success)
        {
            if (!buttonListAliases.Contains(match.Groups["buttonArray"].Value))
            {
                return;
            }

            buttonGroup = match.Groups["arrayButton"];
        }

        if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var index) ||
            !int.TryParse(buttonGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var buttonIndex) ||
            !double.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var x) ||
            !double.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var y))
        {
            return;
        }

        var isLeft = sideLine
            ? match.Groups["dir"].Value.Equals("Left", StringComparison.OrdinalIgnoreCase)
            : !match.Groups["left"].Success || match.Groups["left"].Value == "0";
        calls.Add(new DrawCall(index, offset + match.Index, buttonIndex, x, y, isLeft, blockId));
    }

    private static int FindMatchingDelimiter(string text, int open, char openChar, char closeChar)
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

            if (ch == openChar)
            {
                depth++;
            }
            else if (ch == closeChar && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
