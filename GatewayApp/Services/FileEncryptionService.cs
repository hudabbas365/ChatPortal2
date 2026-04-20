using System.Security.Cryptography;
using System.Text;

namespace GatewayApp.Services;

public sealed class FileEncryptionService
{
    private static byte[] CreateKey()
    {
        var seed = $"{Environment.MachineName}:{Environment.UserName}:AIInsights365Gateway";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = CreateKey();
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var payload = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(payload, 0, payload.Length);
        var ivAndCipher = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, ivAndCipher, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, ivAndCipher, aes.IV.Length, encrypted.Length);
        return Convert.ToBase64String(ivAndCipher);
    }

    public string Decrypt(string encryptedText)
    {
        var ivAndCipher = Convert.FromBase64String(encryptedText);
        using var aes = Aes.Create();
        aes.Key = CreateKey();
        var ivLength = aes.BlockSize / 8;
        var iv = new byte[ivLength];
        var cipher = new byte[ivAndCipher.Length - ivLength];
        Buffer.BlockCopy(ivAndCipher, 0, iv, 0, ivLength);
        Buffer.BlockCopy(ivAndCipher, ivLength, cipher, 0, cipher.Length);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(decrypted);
    }
}
