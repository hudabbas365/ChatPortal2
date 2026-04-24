using AIInsights.Data;
using AIInsights.Models;
using AIInsights.SuperAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace AIInsights.SuperAdmin.Controllers;

// ── View model ──────────────────────────────────────────────────────────────

public class OrgInvoicesViewModel
{
    public Organization Org { get; set; } = null!;
    public List<PaymentRecord> Records { get; set; } = new();

    // Summary (computed on filtered set)
    public decimal TotalPaid { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal TotalOutstanding { get; set; }
    public DateTime? LastPaymentAt { get; set; }
    public decimal Mrr { get; set; }

    // All-time lifetime total (regardless of filter)
    public decimal Lifetime { get; set; }

    // Paging
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    // Active filters (echoed back to the view)
    public string? FilterFrom { get; set; }
    public string? FilterTo { get; set; }
    public string? FilterStatus { get; set; }
    public string? FilterSearch { get; set; }
}

// ── Controller ───────────────────────────────────────────────────────────────

[Authorize]
public class InvoicesController : Controller
{
    private readonly AppDbContext _db;
    private readonly InvoicePdfService _pdf;
    private readonly IInvoiceEmailSender _emailSender;

    public InvoicesController(
        AppDbContext db,
        InvoicePdfService pdf,
        IInvoiceEmailSender emailSender)
    {
        _db = db;
        _pdf = pdf;
        _emailSender = emailSender;
    }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    // ── D2: Per-org invoice list ─────────────────────────────────────────────

    [HttpGet("/superadmin/organizations/{id:int}/invoices")]
    public async Task<IActionResult> OrgInvoices(
        int id,
        [FromQuery] string? from   = null,
        [FromQuery] string? to     = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var org = await _db.Organizations
            .Include(o => o.Users)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (org == null) return NotFound();

        const int pageSize = 50;

        // Base query for this org
        var query = _db.PaymentRecords
            .Where(p => p.OrganizationId == id)
            .AsQueryable();

        // Apply date range filter
        if (DateTime.TryParse(from, out var fromDate))
            query = query.Where(p => p.CreatedAt >= fromDate.Date);
        if (DateTime.TryParse(to, out var toDate))
            query = query.Where(p => p.CreatedAt < toDate.Date.AddDays(1));

        // Apply status filter
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                (p.PayPalOrderId != null && p.PayPalOrderId.ToLower().Contains(term)) ||
                (p.Description   != null && p.Description.ToLower().Contains(term))   ||
                p.PaymentType.ToLower().Contains(term) ||
                (p.InvoiceNumber != null && p.InvoiceNumber.ToLower().Contains(term)));
        }

        // Summary on filtered set
        var allFiltered = await query.ToListAsync();
        var totalPaid = allFiltered
            .Where(p => p.Status is "succeeded" or "paid")
            .Sum(p => p.Amount);
        var totalRefunded = allFiltered
            .Where(p => p.Status == "refunded")
            .Sum(p => p.Amount);
        var totalOutstanding = allFiltered
            .Where(p => p.Status is "pending" or "failed")
            .Sum(p => p.Amount);
        var lastPaymentAt = allFiltered
            .Where(p => p.Status is "succeeded" or "paid")
            .Max(p => (DateTime?)p.CreatedAt);
        var mrr = allFiltered
            .Where(p => p.PaymentType.Contains("subscription", StringComparison.OrdinalIgnoreCase)
                     && p.Status is "succeeded" or "paid")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (decimal?)p.Amount)
            .FirstOrDefault() ?? 0m;

        // Lifetime total (ignores filters)
        var lifetime = await _db.PaymentRecords
            .Where(p => p.OrganizationId == id && (p.Status == "succeeded" || p.Status == "paid"))
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        // Paged results
        var totalCount = allFiltered.Count;
        var records = allFiltered
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var vm = new OrgInvoicesViewModel
        {
            Org             = org,
            Records         = records,
            TotalPaid       = totalPaid,
            TotalRefunded   = totalRefunded,
            TotalOutstanding= totalOutstanding,
            LastPaymentAt   = lastPaymentAt,
            Mrr             = mrr,
            Lifetime        = lifetime,
            Page            = page,
            TotalPages      = (int)Math.Ceiling(totalCount / (double)pageSize),
            TotalCount      = totalCount,
            FilterFrom      = from,
            FilterTo        = to,
            FilterStatus    = status,
            FilterSearch    = search
        };

        return View("~/Views/Admin/OrgInvoices.cshtml", vm);
    }

    // ── D2: CSV export ───────────────────────────────────────────────────────

    [HttpGet("/superadmin/organizations/{id:int}/invoices/export.csv")]
    public async Task<IActionResult> ExportCsv(
        int id,
        [FromQuery] string? from   = null,
        [FromQuery] string? to     = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var query = _db.PaymentRecords
            .Where(p => p.OrganizationId == id)
            .AsQueryable();

        if (DateTime.TryParse(from, out var fromDate))
            query = query.Where(p => p.CreatedAt >= fromDate.Date);
        if (DateTime.TryParse(to, out var toDate))
            query = query.Where(p => p.CreatedAt < toDate.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                (p.PayPalOrderId != null && p.PayPalOrderId.ToLower().Contains(term)) ||
                (p.Description   != null && p.Description.ToLower().Contains(term))   ||
                p.PaymentType.ToLower().Contains(term) ||
                (p.InvoiceNumber != null && p.InvoiceNumber.ToLower().Contains(term)));
        }

        var records = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNumber,CreatedAt,PaymentType,Description,Amount,Currency,Status,PayPalOrderId");

        foreach (var r in records)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(r.InvoiceNumber ?? $"INV-{r.CreatedAt:yyyyMM}-{r.Id:D6}"),
                CsvEscape(r.CreatedAt.ToString("o")),
                CsvEscape(r.PaymentType),
                CsvEscape(r.Description ?? ""),
                CsvEscape(r.Amount.ToString("N2")),
                CsvEscape(r.Currency ?? "USD"),
                CsvEscape(r.Status),
                CsvEscape(r.PayPalOrderId ?? "")));
        }

        var orgSlug = System.Text.RegularExpressions.Regex.Replace(
            org.Name, @"[^a-zA-Z0-9\-]", "-")
            .ToLower()
            .Trim('-');
        var fileName = $"invoices-{orgSlug}-{DateTime.UtcNow:yyyyMMdd}.csv";

        return File(
            Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv",
            fileName);
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ── D3: PDF download ─────────────────────────────────────────────────────

    [HttpGet("/api/superadmin/invoices/{id:int}/pdf")]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var record = await _db.PaymentRecords
            .Include(p => p.Organization)
                .ThenInclude(o => o!.Users)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (record == null || record.Organization == null) return NotFound();

        var pdfBytes = _pdf.Generate(record, record.Organization);
        var invoiceNumber = record.InvoiceNumber
            ?? $"INV-{record.CreatedAt:yyyyMM}-{record.Id:D6}";

        return File(pdfBytes, "application/pdf", $"{invoiceNumber}.pdf");
    }

    // ── D3: Resend email ─────────────────────────────────────────────────────

    [HttpPost("/api/superadmin/invoices/{id:int}/resend-email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendEmail(int id, [FromBody] ResendEmailRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var record = await _db.PaymentRecords
            .Include(p => p.Organization)
                .ThenInclude(o => o!.Users)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (record == null || record.Organization == null)
            return NotFound(new { error = "Invoice not found." });

        var org = record.Organization;
        var invoiceNumber = record.InvoiceNumber
            ?? $"INV-{record.CreatedAt:yyyyMM}-{record.Id:D6}";

        // Determine recipient
        string toEmail;
        if (!string.IsNullOrWhiteSpace(req?.ToEmail))
        {
            toEmail = req.ToEmail.Trim();
        }
        else
        {
            var orgAdmin = org.Users.FirstOrDefault(u => u.Role == "OrgAdmin")
                        ?? org.Users.FirstOrDefault();
            toEmail = orgAdmin?.Email ?? "";
        }

        if (string.IsNullOrEmpty(toEmail))
            return BadRequest(new { error = "No recipient email address available." });

        // Generate PDF
        var pdfBytes = _pdf.Generate(record, org);
        var pdfFileName = $"{invoiceNumber}.pdf";

        // Send email
        var sent = await _emailSender.SendInvoiceEmailAsync(toEmail, invoiceNumber, org.Name, pdfBytes, pdfFileName);

        // Write ActivityLog
        var callerId = GetCurrentUserId() ?? "";
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action       = "Invoice.Resent",
            Description  = $"Invoice {invoiceNumber} resent to {toEmail}",
            UserId       = callerId,
            OrganizationId = org.Id,
            CreatedAt    = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        if (!sent)
            return StatusCode(502, new { error = "Email service unavailable. Activity log was recorded." });

        return Ok(new { success = true, sentTo = toEmail });
    }

    public class ResendEmailRequest
    {
        public string? ToEmail { get; set; }
    }
}
