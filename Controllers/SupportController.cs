using AIInsights.Data;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Route("api/support")]
[ApiController]
public class SupportController : ControllerBase
{
    private readonly ISupportTicketService _tickets;
    private readonly AppDbContext _db;
    private readonly ILogger<SupportController> _logger;

    public SupportController(ISupportTicketService tickets, AppDbContext db, ILogger<SupportController> logger)
    {
        _tickets = tickets;
        _db = db;
        _logger = logger;
    }

    [HttpPost("ticket")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateTicket([FromBody] SupportTicketRequest req)
    {
        if (req == null) return BadRequest(new { error = "Missing request body." });
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "Email, subject and message are required." });

        string? userId = null;
        int? orgId = null;
        string? orgName = null;
        var name = req.Name?.Trim() ?? "";
        var email = req.Email.Trim();

        if (User?.Identity?.IsAuthenticated == true)
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _db.Users
                    .Include(u => u.Organization)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null)
                {
                    orgId = user.OrganizationId;
                    orgName = user.Organization?.Name;
                    if (string.IsNullOrEmpty(name)) name = user.FullName ?? user.UserName ?? email;
                    if (string.IsNullOrEmpty(email)) email = user.Email ?? email;
                }
            }
        }

        try
        {
            var ticket = await _tickets.CreateTicketAsync(
                name, email, req.Subject, req.Message,
                req.Category ?? "Question", req.Priority ?? "Normal",
                userId, orgId, orgName);

            return Ok(new
            {
                success = true,
                ticketNumber = ticket.TicketNumber,
                createdAt = ticket.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create support ticket for {Email}", email);
            return StatusCode(500, new { error = "Could not create ticket. Please try again." });
        }
    }
}

public class SupportTicketRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? Subject { get; set; }
    public string? Message { get; set; }
}
