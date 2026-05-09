namespace AIInsights.Models;

public class BlogSubscription
{
    public int BlogId { get; set; }
    public BlogPost? Blog { get; set; }

    // Stores the selected plan identifier from PlanType so announcements can target plan tiers.
    public int SubscriptionId { get; set; }
}
