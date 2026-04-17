namespace AIInsights.Models;

public class PaymentRecord
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string? UserId { get; set; }
    public string PaymentType { get; set; } = ""; // "subscription", "license", "token_pack", "plan_upgrade"
    public string PaymentMethod { get; set; } = "PayPal"; // "PayPal", "Stripe", etc.
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = ""; // "succeeded", "failed", "pending", "refunded"
    public string? PayPalOrderId { get; set; }
    public string? PayPalSubscriptionId { get; set; }
    public string? Description { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PlanKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
