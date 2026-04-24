namespace AIInsights.SuperAdmin.Services;

public interface IInvoiceEmailSender
{
    /// <summary>
    /// Sends an invoice email with an attached PDF.
    /// </summary>
    Task<bool> SendInvoiceEmailAsync(
        string toEmail,
        string invoiceNumber,
        string orgName,
        byte[] pdfBytes,
        string pdfFileName);
}
