namespace AIInsights.Models;

public class BlogSubscription
{
    public int BlogId { get; set; }
    public int SubscriptionId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public BlogPost? Blog { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public SubscriptionPlan? Subscription { get; set; }
}
