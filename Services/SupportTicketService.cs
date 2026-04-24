using System.Security.Cryptography;
using AIInsights.Data;
using AIInsights.Models;

namespace AIInsights.Services;

public class SupportTicketService : ISupportTicketService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<SupportTicketService> _logger;

    public SupportTicketService(AppDbContext db, IEmailService email, ILogger<SupportTicketService> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    public async Task<SupportTicket> CreateTicketAsync(
        string requesterName,
        string requesterEmail,
        string subject,
        string message,
        string category = "Question",
        string priority = "Normal",
        string? userId = null,
        int? organizationId = null,
        string? orgName = null)
    {
        var ticket = new SupportTicket
        {
            TicketNumber = GenerateTicketNumber(),
            RequesterName = string.IsNullOrWhiteSpace(requesterName) ? requesterEmail : requesterName.Trim(),
            RequesterEmail = requesterEmail.Trim(),
            Subject = subject.Trim(),
            Message = message,
            Category = string.IsNullOrWhiteSpace(category) ? "Question" : category,
            Priority = string.IsNullOrWhiteSpace(priority) ? "Normal" : priority,
            UserId = userId,
            OrganizationId = organizationId,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };

        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync();

        // Fire-and-forget email sends so the API stays snappy and the ticket is
        // already persisted even if SendGrid is throttled or the inbox is down.
        _ = Task.Run(async () =>
        {
            try
            {
                await _email.SendSupportTicketToSupportAsync(
                    ticket.TicketNumber, ticket.RequesterName, ticket.RequesterEmail,
                    ticket.Category, ticket.Priority, ticket.Subject, ticket.Message, orgName);
                await _email.SendSupportTicketAcknowledgmentAsync(
                    ticket.RequesterEmail, ticket.RequesterName, ticket.TicketNumber, ticket.Subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch support ticket emails for {Ticket}", ticket.TicketNumber);
            }
        });

        return ticket;
    }

    private static string GenerateTicketNumber()
    {
        var rnd = RandomNumberGenerator.GetInt32(10000, 99999);
        return $"TCKT-{DateTime.UtcNow:yyyyMMdd}-{rnd}";
    }
}
