namespace AIInsights.Models;

public class PlanChangeLog
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public string? FromPlan { get; set; }
    public string ToPlan { get; set; } = "";

    public int? FromPurchasedLicenses { get; set; }
    public int ToPurchasedLicenses { get; set; }

    public DateTime? FromLicenseEndsAt { get; set; }
    public DateTime? ToLicenseEndsAt { get; set; }

    public string ChangeType { get; set; } = ""; // "PlanChange" | "LicenseGrant" | "LicenseRevoke" | "TermUpdate"
    public string? Reason { get; set; }

    public string? ChangedByUserId { get; set; }
    public string? ChangedByEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}
