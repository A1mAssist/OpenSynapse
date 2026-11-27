namespace OpenSynapse.Core.Help;

public sealed record HelpSearchHit(string TopicId, int Score);

public static class HelpSearch
{
    public static IReadOnlyList<HelpSearchHit> Search(
        string? query,
        Func<string, string> resolveText)
    {
        var terms = (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return [];
        }

        var hits = new List<HelpSearchHit>();
        foreach (var topic in HelpCatalog.Topics)
        {
            var title = resolveText(topic.TitleKey);
            var summary = resolveText(topic.SummaryKey);
            var keywords = resolveText(topic.KeywordsKey);
            var commonTasks = string.Join(
                '\n',
                HelpCatalog.CommonTasks
                    .Where(task => StringComparer.Ordinal.Equals(task.TopicId, topic.Id))
                    .Select(task => resolveText(task.TitleKey)));
            var body = string.Join(
                '\n',
                topic.Blocks.SelectMany(BlockText).Select(resolveText));

            var score = 0;
            var matchesAll = true;
            foreach (var term in terms)
            {
                var termScore = 0;
                if (Contains(title, term)) termScore += 100;
                if (Contains(keywords, term)) termScore += 60;
                if (Contains(commonTasks, term)) termScore += 40;
                if (Contains(summary, term)) termScore += 30;
                if (Contains(body, term)) termScore += 10;
                if (termScore == 0)
                {
                    matchesAll = false;
                    break;
                }

                score += termScore;
            }

            if (matchesAll)
            {
                hits.Add(new HelpSearchHit(topic.Id, score));
            }
        }

        return hits
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.TopicId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> BlockText(HelpBlock block) => block switch
    {
        HelpParagraphBlock paragraph => [paragraph.TextKey],
        HelpStepsBlock steps => steps.Steps.Select(step => step.TextKey),
        HelpCalloutBlock callout => [callout.TextKey],
        HelpFaqBlock faq => faq.Items.SelectMany(item => new[] { item.QuestionKey, item.AnswerKey }),
        _ => [],
    };

    private static bool Contains(string text, string term) =>
        text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
