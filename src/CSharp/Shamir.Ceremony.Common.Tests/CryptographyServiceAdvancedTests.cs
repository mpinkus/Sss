using System.Security;
using System.Security.Cryptography;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class CryptographyServiceAdvancedTests
    {
        private CryptographyService _cryptographyService = null!;

        [TestInitialize]
        public void Setup()
        {
            var securitySettings = new SecuritySettings
            {
                KdfIterations = 1000,
                SecureDeletePasses = 3
            };
            _cryptographyService = new CryptographyService(securitySettings);
        }

        [TestMethod]
        public void EncryptShare_SaltUniqueness_AcrossMultipleEncryptions()
        {
            var share = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var salts = new HashSet<string>();
            for (int i = 0; i < 10; i++)
            {
                var (_, _) = _cryptographyService.EncryptShare(share, password, out string salt, out _);
                salts.Add(salt);
            }

            Assert.AreEqual(10, salts.Count, "All salts should be unique");
        }

        [TestMethod]
        public void EncryptShare_IVUniqueness_AcrossMultipleEncryptions()
        {
            var share = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var ivs = new HashSet<string>();
            for (int i = 0; i < 10; i++)
            {
                var (_, _) = _cryptographyService.EncryptShare(share, password, out _, out string iv);
                ivs.Add(iv);
            }

            Assert.AreEqual(10, ivs.Count, "All IVs should be unique");
        }

        [TestMethod]
        public void EncryptShare_HMACTampering_ShouldBeDetected()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var hmacBytes = Convert.FromBase64String(hmac);
            hmacBytes[0] ^= 0xFF;
            var tamperedHmac = Convert.ToBase64String(hmacBytes);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = tamperedHmac,
                Salt = salt,
                IV = iv
            };

            Assert.ThrowsException<CryptographicException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, password, 1000));
        }

        [TestMethod]
        public void EncryptShare_CiphertextTampering_ShouldBeDetected()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var encryptedBytes = Convert.FromBase64String(encryptedData);
            encryptedBytes[0] ^= 0xFF;
            var tamperedEncrypted = Convert.ToBase64String(encryptedBytes);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = tamperedEncrypted,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            Assert.ThrowsException<CryptographicException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, password, 1000));
        }

        [TestMethod]
        public void EncryptDecrypt_WithUnicodePassword_ShouldSucceed()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("пароль密码🔐");

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
        public void EncryptDecrypt_WithVeryLongShareData_ShouldSucceed()
        {
            var longData = new string('A', 10000);
            var originalShare = new Share { X = 1, Y = longData };
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
        public void EncryptShare_WithDifferentKdfIterations_ShouldProduceDifferentResults()
        {
            var share = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var service1000 = new CryptographyService(new SecuritySettings { KdfIterations = 1000, SecureDeletePasses = 1 });
            var service10000 = new CryptographyService(new SecuritySettings { KdfIterations = 10000, SecureDeletePasses = 1 });

            var (encrypted1, _) = service1000.EncryptShare(share, password, out string salt, out string iv);
            
            var (encrypted2, _) = service10000.EncryptShare(share, password, out _, out _);

            Assert.AreNotEqual(encrypted1, encrypted2);
        }

        [TestMethod]
        public void SecureDelete_MultiplePassesOverwrite_ShouldClearData()
        {
            var data = new byte[1024];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);

            var originalChecksum = ComputeChecksum(data);
            _cryptographyService.SecureDelete(data);
            var afterChecksum = ComputeChecksum(data);

            Assert.AreNotEqual(originalChecksum, afterChecksum);
            Assert.IsTrue(data.All(b => b == 0), "All bytes should be zero after secure delete");
        }

        [TestMethod]
        public void SecureDeleteFile_ExistingFile_ShouldDeleteFile()
        {
            var tempFile = Path.GetTempFileName();
            var testData = new byte[4096];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(testData);
            }
            File.WriteAllBytes(tempFile, testData);

            Assert.IsTrue(File.Exists(tempFile));

            _cryptographyService.SecureDeleteFile(tempFile);

            Assert.IsFalse(File.Exists(tempFile));
        }

        [TestMethod]
        public void SecureDeleteFile_NonExistentFile_ShouldNotThrow()
        {
            var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            
            _cryptographyService.SecureDeleteFile(nonExistentFile);
        }

        [TestMethod]
        public void GenerateRandomSecret_DifferentLengths_ShouldProduceCorrectSizes()
        {
            var lengths = new[] { 1, 16, 32, 64, 128, 256, 512, 1024 };

            foreach (var length in lengths)
            {
                var secret = _cryptographyService.GenerateRandomSecret(length);
                Assert.AreEqual(length, secret.Length, $"Secret length mismatch for {length}");
            }
        }

        [TestMethod]
        public void GenerateRandomSecret_MultipleGenerations_ShouldBeDifferent()
        {
            var secrets = new HashSet<string>();
            
            for (int i = 0; i < 10; i++)
            {
                var secret = _cryptographyService.GenerateRandomSecret(32);
                secrets.Add(Convert.ToBase64String(secret));
            }

            Assert.AreEqual(10, secrets.Count, "All generated secrets should be unique");
        }

        [TestMethod]
        public void CalculateSha256Hash_SameInput_ShouldProduceSameHash()
        {
            var input = "test input string";
            var hash1 = _cryptographyService.CalculateSha256Hash(input);
            var hash2 = _cryptographyService.CalculateSha256Hash(input);

            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void CalculateSha256Hash_DifferentInputs_ShouldProduceDifferentHashes()
        {
            var input1 = "test input 1";
            var input2 = "test input 2";
            var hash1 = _cryptographyService.CalculateSha256Hash(input1);
            var hash2 = _cryptographyService.CalculateSha256Hash(input2);

            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void CalculateSha256Hash_EmptyString_ShouldProduceValidHash()
        {
            var hash = _cryptographyService.CalculateSha256Hash(string.Empty);
            
            Assert.IsFalse(string.IsNullOrEmpty(hash));
            Assert.AreEqual(44, hash.Length);
        }

        [TestMethod]
        public void CalculateSha256Hash_UnicodeString_ShouldHandleCorrectly()
        {
            var input = "Hello 世界 🌍 тест";
            var hash1 = _cryptographyService.CalculateSha256Hash(input);
            var hash2 = _cryptographyService.CalculateSha256Hash(input);

            Assert.AreEqual(hash1, hash2);
            Assert.IsFalse(string.IsNullOrEmpty(hash1));
        }

        [TestMethod]
        public void DecryptShare_WithIncorrectIVLength_ShouldThrow()
        {
            var originalShare = new Share { X = 1, Y = "test-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out _);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = Convert.ToBase64String(new byte[8]) // Wrong IV length (should be 12)
            };

            Assert.ThrowsException<ArgumentException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, password, 1000));
        }

        [TestMethod]
        public void EncryptDecrypt_ConcurrentOperations_ShouldSucceed()
        {
            var shares = Enumerable.Range(1, 10).Select(i => new Share { X = i, Y = $"share-{i}" }).ToList();
            var password = CreateSecureString("testpassword123");
            var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

            Parallel.ForEach(shares, share =>
            {
                try
                {
                    var (encryptedData, hmac) = _cryptographyService.EncryptShare(share, password, out string salt, out string iv);

                    var keeperRecord = new SecretKeeperRecord
                    {
                        EncryptedShare = encryptedData,
                        Hmac = hmac,
                        Salt = salt,
                        IV = iv
                    };

                    var decryptedShare = _cryptographyService.DecryptShare(keeperRecord, password, 1000);

                    results.Add(decryptedShare.X == share.X && decryptedShare.Y == share.Y);
                }
                catch
                {
                    results.Add(false);
                }
            });

            Assert.IsTrue(results.All(r => r), "All concurrent operations should succeed");
        }

        [TestMethod]
        public void EncryptShare_WithSpecialCharactersInShareData_ShouldSucceed()
        {
            var specialChars = "!@#$%^&*()_+-=[]{}|;':\",./<>?`~\n\r\t";
            var originalShare = new Share { X = 1, Y = specialChars };
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
        public void SecureDelete_WithLargeArray_ShouldComplete()
        {
            var data = new byte[1024 * 1024]; // 1 MB
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data);
            }

            var originalData = new byte[data.Length];
            Array.Copy(data, originalData, data.Length);

            _cryptographyService.SecureDelete(data);

            CollectionAssert.AreNotEqual(originalData, data);
            Assert.IsTrue(data.All(b => b == 0));
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

        private static string ComputeChecksum(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(data));
        }
    }
}
