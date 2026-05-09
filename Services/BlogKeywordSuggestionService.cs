using System.Text.RegularExpressions;

namespace AIInsights.Services;

public class BlogKeywordSuggestionService : IBlogKeywordSuggestionService
{
    private static readonly Regex HtmlRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","about","after","all","also","an","and","any","are","as","at","be","because","been","before","being","but","by","can","could",
        "did","do","does","each","for","from","get","got","had","has","have","he","her","here","him","his","how","i","if","in","into","is",
        "it","its","just","may","me","more","most","my","new","no","not","of","on","one","only","or","our","out","over","same","she","so",
        "some","such","than","that","the","their","them","then","there","these","they","this","to","too","up","use","using","very","was","we",
        "were","what","when","where","which","while","who","why","will","with","you","your"
    };

    public IReadOnlyList<string> SuggestKeywords(string? title, string? htmlContent, int minimumCount = 15)
    {
        var combined = string.Join(' ', new[] { title ?? string.Empty, StripHtml(htmlContent) })
            .ToLowerInvariant();
        var tokens = TokenRegex.Matches(combined)
            .Select(m => m.Value)
            .Where(token => token.Length > 2 && !StopWords.Contains(token))
            .ToList();

        var scores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        for (var gramLength = 1; gramLength <= 3; gramLength++)
        {
            for (var index = 0; index <= tokens.Count - gramLength; index++)
            {
                var phraseTokens = tokens.Skip(index).Take(gramLength).ToArray();
                if (phraseTokens.Any(StopWords.Contains))
                {
                    continue;
                }

                var phrase = string.Join(' ', phraseTokens).Trim();
                if (phrase.Length < 3)
                {
                    continue;
                }

                scores.TryGetValue(phrase, out var existing);
                scores[phrase] = existing + gramLength;
            }
        }

        var ranked = scores
            .OrderByDescending(kvp => kvp.Value)
            .ThenByDescending(kvp => kvp.Key.Length)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ranked.Count < minimumCount)
        {
            ranked.AddRange(tokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(token => ranked.All(existing => !existing.Equals(token, StringComparison.OrdinalIgnoreCase))));
        }

        if (ranked.Count < minimumCount)
        {
            var titleTokens = TokenRegex.Matches((title ?? string.Empty).ToLowerInvariant())
                .Select(m => m.Value)
                .Where(token => token.Length > 2 && !StopWords.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var gramLength = 2; gramLength <= Math.Min(3, titleTokens.Length); gramLength++)
            {
                for (var index = 0; index <= titleTokens.Length - gramLength; index++)
                {
                    var candidate = string.Join(' ', titleTokens.Skip(index).Take(gramLength));
                    if (ranked.All(existing => !existing.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    {
                        ranked.Add(candidate);
                    }
                }
            }
        }

        // Super Admin SEO requires at least 15 keyword suggestions, so the helper
        // keeps that floor even when a smaller minimumCount is requested.
        return ranked.Take(Math.Max(minimumCount, 15)).ToList();
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return HtmlRegex.Replace(html, " ");
    }
}
