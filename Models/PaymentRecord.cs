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

    // ── Phase E: Richer invoice details (all optional for back-compat) ──
    public string? InvoiceNumber { get; set; }       // "INV-YYYYMMDD-xxxxx"
    public int? Quantity { get; set; }               // e.g. 3 licenses, 1 token pack
    public decimal? UnitPrice { get; set; }          // per-unit price
    public decimal? Subtotal { get; set; }           // before tax
    public decimal? TaxAmount { get; set; }
    public string? TaxRegion { get; set; }
    public decimal? TaxRatePercent { get; set; }
    public long? TokensAdded { get; set; }           // for token packs
    public string? BillingName { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingCompany { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }
    public string? LineItemsJson { get; set; }       // JSON array for flexibility
    public string? PayerEmail { get; set; }          // from PayPal payer
    public string? PayerName { get; set; }
    public string? CaptureId { get; set; }           // PayPal capture id
    public string? PdfPath { get; set; }             // if we stash a generated PDF
    public DateTime? PaidAt { get; set; }
}

