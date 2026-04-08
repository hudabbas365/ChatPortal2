namespace ChatPortal2.Models;

public enum PlanType { Free, FreeTrial, Professional, Enterprise }

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public PlanType Plan { get; set; } = PlanType.Free;
    public DateTime? TrialStartDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public bool HasUsedTrial { get; set; } = false;
    public bool IsTrialActive => Plan == PlanType.FreeTrial && TrialEndDate.HasValue && DateTime.UtcNow <= TrialEndDate.Value;
    public bool IsTrialExpired => HasUsedTrial && (!IsTrialActive);
    public int DaysRemaining => IsTrialActive ? Math.Max(0, (int)(TrialEndDate!.Value - DateTime.UtcNow).TotalDays) : 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ApplicationUser? User { get; set; }
}
