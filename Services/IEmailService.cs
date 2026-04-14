namespace AIInsights.Services;

public interface IEmailService
{
    Task<bool> SendCredentialsEmailAsync(string toEmail, string fullName, string username, string password, string loginUrl);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string fullName, string newPassword, string loginUrl);
}
