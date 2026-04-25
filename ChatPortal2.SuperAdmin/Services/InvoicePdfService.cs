using AIInsights.Models;
using QuestPDF.Elements.Table;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIInsights.SuperAdmin.Services;

public class InvoicePdfService
{
    private const string CompanyName = "AIInsights365";
    private const string CompanyEmail = "support@aiinsights365.net";

    public byte[] Generate(PaymentRecord record, Organization org)
    {
        var invoiceNumber = record.InvoiceNumber
            ?? $"INV-{record.CreatedAt:yyyyMM}-{record.Id:D6}";

        var orgAdmin = org.Users.FirstOrDefault(u => u.Role == "OrgAdmin")
                    ?? org.Users.FirstOrDefault();

        var billToName    = record.BillingName    ?? orgAdmin?.FullName ?? org.Name;
        var billToEmail   = record.BillingEmail   ?? orgAdmin?.Email    ?? "";
        var billToCompany = record.BillingCompany ?? org.Name;

        var description = record.Description ?? record.PaymentType;
        var qty        = record.Quantity  ?? 1;
        var unitPrice  = record.UnitPrice ?? record.Amount;
        var subtotal   = record.Subtotal  ?? record.Amount;
        var tax        = record.TaxAmount ?? 0m;
        var total      = subtotal + tax;
        var currency   = record.Currency ?? "USD";

        var statusLabel = record.Status switch
        {
            "succeeded" => "PAID",
            "refunded"  => "REFUNDED",
            "pending"   => "PENDING",
            "failed"    => "FAILED",
            _           => record.Status.ToUpperInvariant()
        };
        var statusColor = statusLabel switch
        {
            "PAID"     => "#059669",
            "REFUNDED" => "#0284c7",
            "FAILED"   => "#e11d48",
            _          => "#d97706"
        };

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#2d3748"));

                // ── Header ──
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(inner =>
                        {
                            inner.Item().Text(CompanyName).FontSize(22).Bold().FontColor("#1e3a5f");
                            inner.Item().Text(CompanyEmail).FontSize(9).FontColor("#8596a9");
                        });
                        row.ConstantItem(140).Column(inner =>
                        {
                            inner.Item().AlignRight().Text("INVOICE").FontSize(18).Bold().FontColor("#4a8ec9");
                            inner.Item().AlignRight().Text(invoiceNumber).FontSize(9).FontColor("#596d82");
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#cdd8e4");
                });

                // ── Content ──
                page.Content().PaddingTop(20).Column(col =>
                {
                    // Meta row: dates + status
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Invoice Date").Bold().FontSize(8).FontColor("#8596a9");
                            c.Item().Text(record.CreatedAt.ToString("MMMM dd, yyyy"));
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Due Date").Bold().FontSize(8).FontColor("#8596a9");
                            c.Item().Text(record.DueDate.HasValue
                                ? record.DueDate.Value.ToString("MMMM dd, yyyy")
                                : "Upon Receipt");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Paid At").Bold().FontSize(8).FontColor("#8596a9");
                            c.Item().Text(record.PaidAt.HasValue
                                ? record.PaidAt.Value.ToString("MMM dd, yyyy HH:mm") + " UTC"
                                : "—");
                        });
                        row.ConstantItem(80).Column(c =>
                        {
                            c.Item().AlignRight().Text("Status").Bold().FontSize(8).FontColor("#8596a9");
                            c.Item().AlignRight().Text(statusLabel).Bold().FontColor(statusColor);
                        });
                    });

                    // From / Bill-to
                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FROM").Bold().FontSize(8).FontColor("#8596a9");
                            c.Item().Text(CompanyName).Bold();
                            c.Item().Text(CompanyEmail).FontColor("#596d82");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BILL TO").Bold().FontSize(8).FontColor("#8596a9");
                            c.Item().Text(billToCompany).Bold();
                            if (!string.IsNullOrEmpty(billToName))
                                c.Item().Text(billToName).FontColor("#596d82");
                            if (!string.IsNullOrEmpty(billToEmail))
                                c.Item().Text(billToEmail).FontColor("#596d82");
                        });
                    });

                    // Line items table
                    col.Item().PaddingTop(24).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(5);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            void H(string text) => header.Cell()
                                .Background("#f0f4f8").Padding(6)
                                .Text(text).Bold().FontSize(8).FontColor("#8596a9");
                            H("DESCRIPTION"); H("QTY"); H("UNIT PRICE"); H("AMOUNT");
                        });

                        void D(ITableCellContainer cell, string text, bool right = false)
                        {
                            var content = cell.BorderBottom(1).BorderColor("#f0f4f8").Padding(6);
                            if (right) content.AlignRight().Text(text);
                            else       content.Text(text);
                        }

                        D(table.Cell(), $"{record.PaymentType} — {description}");
                        D(table.Cell(), qty.ToString(), true);
                        D(table.Cell(), $"{currency} {unitPrice:N2}", true);
                        D(table.Cell(), $"{currency} {unitPrice * qty:N2}", true);
                    });

                    // Totals
                    col.Item().PaddingTop(12).AlignRight().Column(c =>
                    {
                        c.Item().Row(r =>
                        {
                            r.ConstantItem(120).AlignRight().Text("Subtotal").FontSize(9).FontColor("#596d82");
                            r.ConstantItem(100).AlignRight().Text($"{currency} {subtotal:N2}");
                        });
                        c.Item().Row(r =>
                        {
                            r.ConstantItem(120).AlignRight().Text("Tax").FontSize(9).FontColor("#596d82");
                            r.ConstantItem(100).AlignRight().Text($"{currency} {tax:N2}");
                        });
                        c.Item().PaddingTop(4).LineHorizontal(1).LineColor("#cdd8e4");
                        c.Item().PaddingTop(4).Row(r =>
                        {
                            r.ConstantItem(120).AlignRight().Text("TOTAL").Bold().FontSize(11);
                            r.ConstantItem(100).AlignRight().Text($"{currency} {total:N2}").Bold().FontSize(11);
                        });
                    });

                    // Payment reference
                    if (!string.IsNullOrEmpty(record.PayPalOrderId) || !string.IsNullOrEmpty(record.CaptureId))
                    {
                        col.Item().PaddingTop(30).Column(c =>
                        {
                            c.Item().Text("Payment Reference").Bold().FontSize(8).FontColor("#8596a9");
                            if (!string.IsNullOrEmpty(record.PayPalOrderId))
                                c.Item().Text($"PayPal Order ID: {record.PayPalOrderId}").FontSize(9).FontColor("#596d82");
                            if (!string.IsNullOrEmpty(record.CaptureId))
                                c.Item().Text($"Capture ID: {record.CaptureId}").FontSize(9).FontColor("#596d82");
                        });
                    }
                });

                // ── Footer ──
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span($"{CompanyName} · {CompanyEmail} · Invoice {invoiceNumber}  ·  Page ")
                        .FontSize(8).FontColor("#8596a9");
                    text.CurrentPageNumber().FontSize(8).FontColor("#8596a9");
                    text.Span(" of ").FontSize(8).FontColor("#8596a9");
                    text.TotalPages().FontSize(8).FontColor("#8596a9");
                });
            });
        });

        return doc.GeneratePdf();
    }
}
