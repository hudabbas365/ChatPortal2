using AIInsights.Models;

namespace AIInsights.Services;

public interface ISupportTicketService
{
    Task<SupportTicket> CreateTicketAsync(
        string requesterName,
        string requesterEmail,
        string subject,
        string message,
        string category = "Question",
        string priority = "Normal",
        string? userId = null,
        int? organizationId = null,
        string? orgName = null);
}
