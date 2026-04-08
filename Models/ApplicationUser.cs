using Microsoft.AspNetCore.Identity;

namespace ChatPortal2.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "User"; // SuperAdmin, OrgAdmin, User
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public SubscriptionPlan? Subscription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
