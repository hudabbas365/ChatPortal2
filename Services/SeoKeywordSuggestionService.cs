using System.Text.RegularExpressions;

namespace AIInsights.Services;

public interface ISeoKeywordSuggestionService
{
    IReadOnlyList<string> SuggestKeywords(string? title, string? htmlContent, int minCount = 15);
}

public class SeoKeywordSuggestionService : ISeoKeywordSuggestionService
{
    private static readonly Regex HtmlRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","and","are","as","at","be","been","being","but","by","can","did","do","does","for","from",
        "had","has","have","he","her","hers","him","his","how","i","if","in","into","is","it","its","just",
        "me","my","no","not","of","on","or","our","out","over","she","so","than","that","the","their","them",
        "then","there","these","they","this","to","too","up","was","we","were","what","when","where","which",
        "who","why","will","with","you","your"
    };

    public IReadOnlyList<string> SuggestKeywords(string? title, string? htmlContent, int minCount = 15)
    {
        var normalized = NormalizeText($"{title} {htmlContent}");
        var tokens = TokenRegex.Matches(normalized)
            .Select(m => m.Value)
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToList();

        var counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        AddNGramCounts(tokens, 1, counts);
        AddNGramCounts(tokens, 2, counts);
        AddNGramCounts(tokens, 3, counts);

        var suggestions = counts
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(minCount, 15))
            .ToList();

        if (suggestions.Count >= minCount) return suggestions;

        foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!suggestions.Contains(token, StringComparer.OrdinalIgnoreCase))
                suggestions.Add(token);
            if (suggestions.Count >= minCount) break;
        }

        return suggestions;
    }

    private static string NormalizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var withoutHtml = HtmlRegex.Replace(input, " ");
        return withoutHtml.ToLowerInvariant();
    }

    private static void AddNGramCounts(IReadOnlyList<string> tokens, int phraseLength, IDictionary<string, double> counts)
    {
        if (tokens.Count < phraseLength) return;
        for (var i = 0; i <= tokens.Count - phraseLength; i++)
        {
            var words = tokens.Skip(i).Take(phraseLength).ToArray();
            if (words.Any(w => w.Length < 2)) continue;
            var phrase = string.Join(' ', words);
            if (!counts.TryGetValue(phrase, out var current))
                current = 0;
            counts[phrase] = current + (1 * phraseLength);
        }
    }
}
