namespace AIInsights.Services;

public interface IBlogKeywordSuggestionService
{
    IReadOnlyList<string> SuggestKeywords(string? title, string? htmlContent, int minimumCount = 15);
}
