using AIInsights.Services;

namespace ChatPortal2.Tests;

public class KeywordSuggestionServiceTests
{
    [Fact]
    public void SuggestKeywords_ReturnsAtLeastFifteenUniqueKeywords()
    {
        var service = new BlogKeywordSuggestionService();
        var html = @"<h1>Power BI launch checklist</h1><p>This feature announcement explains how retail analytics teams can connect Power BI dashboards, schedule refreshes, configure workspace security, publish charts, monitor usage, and automate weekly reporting across finance, operations, sales, and inventory planning.</p><p>The article covers dataset refresh monitoring, role-based access, executive dashboards, anomaly detection, usage insights, KPI alerts, and rollout planning for enterprise reporting.</p>";

        var keywords = service.SuggestKeywords("Power BI feature launch guide for enterprise reporting", html);

        Assert.True(keywords.Count >= 15);
        Assert.Equal(keywords.Count, keywords.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
