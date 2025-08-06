using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Configuration;

namespace Shamir.Ceremony.Common.Services;

public class CryptographyService
{
    private readonly SecuritySettings _securitySettings;

    public CryptographyService(SecuritySettings securitySettings)
    {
        _securitySettings = securitySettings;
    }

    public byte[] GenerateRandomSecret(int length)
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);
        return bytes;
    }

    public (string encryptedData, string hmac) EncryptShare(Share share, SecureString password, out string salt, out string iv)
    {
        var saltBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }
        salt = Convert.ToBase64String(saltBytes);

        byte[] passwordBytes = SecureStringToBytes(password);

        using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, saltBytes, _securitySettings.KdfIterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);
        var hmacKey = pbkdf2.GetBytes(32);

        var ivBytes = new byte[12]; // AES-GCM requires 12-byte nonce
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(ivBytes);
        }
        iv = Convert.ToBase64String(ivBytes);

        var shareJson = JsonSerializer.Serialize(share);
        var plaintext = Encoding.UTF8.GetBytes(shareJson);

        using var aesGcm = new AesGcm(key);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aesGcm.Encrypt(ivBytes, plaintext, ciphertext, tag);

        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        using var hmac = new HMACSHA256(hmacKey);
        var hmacValue = hmac.ComputeHash(combined);

        SecureDelete(passwordBytes);
        SecureDelete(key);
        SecureDelete(hmacKey);
        SecureDelete(plaintext);

        return (Convert.ToBase64String(combined), Convert.ToBase64String(hmacValue));
    }

    public Share DecryptShare(SecretKeeperRecord keeper, SecureString password, int kdfIterations)
    {
        var saltBytes = Convert.FromBase64String(keeper.Salt);
        var ivBytes = Convert.FromBase64String(keeper.IV);
        if (ivBytes.Length != 12) // AES-GCM requires 12-byte nonce
        {
            throw new ArgumentException("Invalid IV length");
        }
        var combined = Convert.FromBase64String(keeper.EncryptedShare);
        var storedHmac = Convert.FromBase64String(keeper.Hmac);

        byte[] passwordBytes = SecureStringToBytes(password);

        using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, saltBytes, kdfIterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);
        var hmacKey = pbkdf2.GetBytes(32);

        using var hmac = new HMACSHA256(hmacKey);
        var computedHmac = hmac.ComputeHash(combined);

        if (!computedHmac.SequenceEqual(storedHmac))
        {
            SecureDelete(passwordBytes);
            SecureDelete(key);
            SecureDelete(hmacKey);
            throw new CryptographicException("HMAC verification failed");
        }

        var ciphertext = new byte[combined.Length - 16];
        var tag = new byte[16];
        Buffer.BlockCopy(combined, 0, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(combined, ciphertext.Length, tag, 0, 16);

        using var aesGcm = new AesGcm(key);
        var plaintext = new byte[ciphertext.Length];
        aesGcm.Decrypt(ivBytes, ciphertext, tag, plaintext);

        var shareJson = Encoding.UTF8.GetString(plaintext);
        var share = JsonSerializer.Deserialize<Share>(shareJson);

        SecureDelete(passwordBytes);
        SecureDelete(key);
        SecureDelete(hmacKey);
        SecureDelete(plaintext);

        return share ?? throw new InvalidOperationException("Failed to deserialize share");
    }

    public void SecureDelete(byte[] data)
    {
        if (data == null) return;

        using var rng = RandomNumberGenerator.Create();
        for (int i = 0; i < _securitySettings.SecureDeletePasses; i++)
        {
            rng.GetBytes(data);
        }
        Array.Clear(data, 0, data.Length);
    }

    public void SecureDeleteFile(string filepath)
    {
        if (!File.Exists(filepath)) return;

        try
        {
            var fileInfo = new FileInfo(filepath);
            long fileSize = fileInfo.Length;

            using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[4096];
                using var rng = RandomNumberGenerator.Create();

                for (int pass = 0; pass < _securitySettings.SecureDeletePasses; pass++)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    long written = 0;

                    while (written < fileSize)
                    {
                        rng.GetBytes(buffer);
                        int toWrite = (int)Math.Min(buffer.Length, fileSize - written);
                        stream.Write(buffer, 0, toWrite);
                        written += toWrite;
                    }
                    stream.Flush();
                }
            }

            File.Delete(filepath);
        }
        catch (Exception)
        {
        }
    }

    public string CalculateSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static byte[] SecureStringToBytes(SecureString secureString)
    {
        string str = SecureStringToString(secureString);
        byte[] bytes = Encoding.UTF8.GetBytes(str);
        return bytes;
    }

    private static string SecureStringToString(SecureString secureString)
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
}
