using Microsoft.AspNetCore.Identity;

namespace AIInsights.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "User"; // SuperAdmin, OrgAdmin, User
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public SubscriptionPlan? Subscription { get; set; }
    public string Status { get; set; } = "Active"; // Active, Pending, Suspended
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? StripeCustomerId { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    // Last-login geo fields (D25)
    public string? LastLoginIp { get; set; }
    public string? LastLoginCountry { get; set; }  // ISO-3166 alpha-2
    public string? LastLoginCity { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Force-password-reset flag (D22)
    public bool MustChangePassword { get; set; } = false;
}
