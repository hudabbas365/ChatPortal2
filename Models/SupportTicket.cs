using System.ComponentModel.DataAnnotations;

namespace AIInsights.Models;

public class SupportTicket
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string TicketNumber { get; set; } = string.Empty;

    public int? OrganizationId { get; set; }
    public string? UserId { get; set; }

    [Required, MaxLength(160)]
    public string RequesterName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string RequesterEmail { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Category { get; set; } = "Question";

    [MaxLength(20)]
    public string Priority { get; set; } = "Normal";

    [Required, MaxLength(250)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = "Open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    [MaxLength(450)]
    public string? AssignedToUserId { get; set; }

    public string? Response { get; set; }
}
