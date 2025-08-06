using System.Security;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class CryptographyServiceComprehensiveTests
    {
        private CryptographyService _cryptographyService = null!;

        [TestInitialize]
        public void Setup()
        {
            var securitySettings = new SecuritySettings
            {
                KdfIterations = 1000,
                SecureDeletePasses = 1
            };
            _cryptographyService = new CryptographyService(securitySettings);
        }

        [TestMethod]
        public void GenerateRandomSecret_ShouldReturnCorrectLength()
        {
            var secret = _cryptographyService.GenerateRandomSecret(32);
            Assert.AreEqual(32, secret.Length);
        }

        [TestMethod]
        public void GenerateRandomSecret_ShouldReturnDifferentValues()
        {
            var secret1 = _cryptographyService.GenerateRandomSecret(16);
            var secret2 = _cryptographyService.GenerateRandomSecret(16);
            Assert.IsFalse(secret1.SequenceEqual(secret2));
        }

        [TestMethod]
        public void EncryptDecryptShare_ShouldRoundTrip()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            var decryptedShare = _cryptographyService.DecryptShare(keeperRecord, password, 1000);

            Assert.AreEqual(originalShare.X, decryptedShare.X);
            Assert.AreEqual(originalShare.Y, decryptedShare.Y);
        }

        [TestMethod]
        public void DecryptShare_WithWrongPassword_ShouldThrow()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var correctPassword = CreateSecureString("correctpassword");
            var wrongPassword = CreateSecureString("wrongpassword");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, correctPassword, out string salt, out string iv);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            Assert.ThrowsException<System.Security.Cryptography.CryptographicException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, wrongPassword, 1000));
        }

        [TestMethod]
        public void DecryptShare_WithInvalidIV_ShouldThrow()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = "invalid-iv"
            };

            Assert.ThrowsException<FormatException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, password, 1000));
        }

        [TestMethod]
        public void CalculateSha256Hash_ShouldReturnConsistentHash()
        {
            var input = "test input string";
            var hash1 = _cryptographyService.CalculateSha256Hash(input);
            var hash2 = _cryptographyService.CalculateSha256Hash(input);
            
            Assert.AreEqual(hash1, hash2);
            Assert.IsFalse(string.IsNullOrEmpty(hash1));
        }

        [TestMethod]
        public void CalculateSha256Hash_DifferentInputs_ShouldReturnDifferentHashes()
        {
            var input1 = "test input string 1";
            var input2 = "test input string 2";
            var hash1 = _cryptographyService.CalculateSha256Hash(input1);
            var hash2 = _cryptographyService.CalculateSha256Hash(input2);
            
            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void SecureDelete_ShouldClearArray()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var originalData = new byte[data.Length];
            Array.Copy(data, originalData, data.Length);

            _cryptographyService.SecureDelete(data);

            Assert.IsFalse(data.SequenceEqual(originalData));
            Assert.IsTrue(data.All(b => b == 0));
        }

        [TestMethod]
        public void SecureDelete_WithNullArray_ShouldNotThrow()
        {
            _cryptographyService.SecureDelete(null!);
        }

        [TestMethod]
        public void SecureDeleteFile_WithNonExistentFile_ShouldNotThrow()
        {
            var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent.txt");
            _cryptographyService.SecureDeleteFile(nonExistentFile);
        }

        [TestMethod]
        public void SecureDeleteFile_WithValidFile_ShouldDeleteFile()
        {
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "test content");
            
            Assert.IsTrue(File.Exists(tempFile));
            
            _cryptographyService.SecureDeleteFile(tempFile);
            
            Assert.IsFalse(File.Exists(tempFile));
        }

        private static SecureString CreateSecureString(string value)
        {
            var secureString = new SecureString();
            foreach (char c in value)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
    }
}
