using Microsoft.AspNetCore.DataProtection;

namespace ChatPortal2.Services;

public interface IDataProtectionService
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

public class DataProtectionService : IDataProtectionService
{
    private readonly IDataProtector _protector;

    public DataProtectionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ChatPortal2.DatasourceCredentials");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch
        {
            // Return as-is for values that were stored before encryption was introduced
            return ciphertext;
        }
    }
}
