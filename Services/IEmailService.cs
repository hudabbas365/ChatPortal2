namespace AIInsights.Services;

public interface IEmailService
{
    Task<bool> SendCredentialsEmailAsync(string toEmail, string fullName, string username, string password, string loginUrl);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string fullName, string newPassword, string loginUrl);
    Task<bool> SendEmailConfirmationAsync(string toEmail, string fullName, string confirmUrl);
    Task<bool> SendForgotPasswordEmailAsync(string toEmail, string fullName, string resetUrl);
    Task<bool> SendInvoiceEmailAsync(string toEmail, string fullName, string orgName, string description, decimal amount, string currency, string paymentId, DateTime date);
}
