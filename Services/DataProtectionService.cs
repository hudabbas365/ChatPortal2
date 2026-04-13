using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace ChatPortal2.Services;

public interface IDataProtectionService
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

public class DataProtectionService : IDataProtectionService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionService> _logger;

    public DataProtectionService(IDataProtectionProvider provider, ILogger<DataProtectionService> logger)
    {
        _protector = provider.CreateProtector("ChatPortal2.DatasourceCredentials");
        _logger = logger;
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Value was likely stored before encryption was introduced — return as-is for backward compatibility
            return ciphertext;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error decrypting a datasource credential value.");
            throw;
        }
    }
}
