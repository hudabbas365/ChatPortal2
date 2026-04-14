using System.Security.Cryptography;

namespace ChatPortal2.Services;

/// <summary>
/// AES-256-CBC encryption service for sensitive datasource fields.
/// The encryption key is read from configuration ("Encryption:Key") and must be
/// a Base64-encoded 32-byte key.
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(IConfiguration configuration)
    {
        var keyBase64 = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key is not configured in appsettings.");
        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption:Key must be a Base64-encoded 32-byte (256-bit) value.");
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV (16 bytes) to cipher text so we can decrypt later
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        byte[] fullCipher;
        try
        {
            fullCipher = Convert.FromBase64String(cipherText);
        }
        catch (FormatException)
        {
            // Not a valid Base64 string — return as-is (likely unencrypted legacy data)
            return cipherText;
        }

        // IV is 16 bytes; minimum valid payload is IV + at least one AES block (16 bytes)
        if (fullCipher.Length < 32)
            return cipherText;

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        try
        {
            using var decryptor = aes.CreateDecryptor();
            var cipherBytes = new byte[fullCipher.Length - 16];
            Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // Decryption failed — likely unencrypted legacy data
            return cipherText;
        }
    }
}
