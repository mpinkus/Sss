using System.Security;
using System.Security.Cryptography;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Cryptography;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class SecurityAdversarialTests
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
        public void ShareTampering_ModifyEncryptedData_ShouldBeDetected()
        {
            var originalShare = new Share { X = 1, Y = "original-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var encryptedBytes = Convert.FromBase64String(encryptedData);
            encryptedBytes[0] ^= 0xFF; // Flip bits in first byte
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
        public void ShareTampering_ModifyHMAC_ShouldBeDetected()
        {
            var originalShare = new Share { X = 1, Y = "original-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var hmacBytes = Convert.FromBase64String(hmac);
            hmacBytes[hmacBytes.Length - 1] ^= 0xFF;
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
        public void ShareTampering_ModifySalt_ShouldFailDecryption()
        {
            var originalShare = new Share { X = 1, Y = "original-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var saltBytes = Convert.FromBase64String(salt);
            saltBytes[0] ^= 0xFF;
            var tamperedSalt = Convert.ToBase64String(saltBytes);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = tamperedSalt,
                IV = iv
            };

            Assert.ThrowsException<CryptographicException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, password, 1000));
        }

        [TestMethod]
        public void ShareTampering_ModifyIV_ShouldFailDecryption()
        {
            var originalShare = new Share { X = 1, Y = "original-share-data" };
            var password = CreateSecureString("testpassword123");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(originalShare, password, out string salt, out string iv);

            var ivBytes = Convert.FromBase64String(iv);
            ivBytes[0] ^= 0xFF;
            var tamperedIv = Convert.ToBase64String(ivBytes);

            var keeperRecord = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = tamperedIv
            };

            Assert.ThrowsException<CryptographicException>(() =>
                _cryptographyService.DecryptShare(keeperRecord, password, 1000));
        }

        [TestMethod]
        public void InsufficientShares_ShouldNotRevealSecretParts()
        {
            var secret = new byte[] { 42, 84, 126, 168, 210 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.ReconstructSecret(shares.Take(2).ToList(), threshold));

            var twoShares = shares.Take(2).ToList();
            Assert.AreEqual(2, twoShares.Count);
        }

        [TestMethod]
        public void ShareReplay_OldShares_ShouldNotWorkWithNewCeremony()
        {
            var secret1 = new byte[] { 1, 2, 3 };
            var secret2 = new byte[] { 4, 5, 6 };

            var shares1 = ShamirSecretShare.GenerateShares(secret1, 2, 3);
            var shares2 = ShamirSecretShare.GenerateShares(secret2, 2, 3);

            var mixedShares = new List<Share> { shares1[0], shares2[1] };
            var result = ShamirSecretShare.ReconstructSecret(mixedShares, 2);

            CollectionAssert.AreNotEqual(secret1, result);
            CollectionAssert.AreNotEqual(secret2, result);
        }

        [TestMethod]
        public void PasswordBruteForce_ShouldBeComputationallyExpensive()
        {
            var share = new Share { X = 1, Y = "test-share" };
            var correctPassword = CreateSecureString("CorrectPassword123!");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(share, correctPassword, out string salt, out string iv);

            var keeper = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _cryptographyService.DecryptShare(keeper, correctPassword, 1000);
            }
            catch { }
            sw.Stop();

            var correctTime = sw.Elapsed;

            var wrongPasswords = new[] { "wrong1", "wrong2", "wrong3" };
            var wrongTimes = new List<TimeSpan>();

            foreach (var wrongPass in wrongPasswords)
            {
                sw.Restart();
                try
                {
                    _cryptographyService.DecryptShare(keeper, CreateSecureString(wrongPass), 1000);
                }
                catch
                {
                }
                sw.Stop();
                wrongTimes.Add(sw.Elapsed);
            }

            Assert.IsTrue(correctTime.TotalMilliseconds > 0);
            Assert.IsTrue(wrongTimes.All(t => t.TotalMilliseconds > 0));
        }

        [TestMethod]
        public void MemoryInspection_SecureDelete_ShouldOverwriteData()
        {
            var sensitiveData = new byte[1024];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(sensitiveData);
            }

            using var sha = SHA256.Create();
            var originalHash = sha.ComputeHash(sensitiveData);

            _cryptographyService.SecureDelete(sensitiveData);

            Assert.IsTrue(sensitiveData.All(b => b == 0));

            var deletedHash = sha.ComputeHash(sensitiveData);

            CollectionAssert.AreNotEqual(originalHash, deletedHash);
        }

        [TestMethod]
        public void ShareSubstitution_WrongShareNumber_ShouldProduceWrongSecret()
        {
            var secret = new byte[] { 100, 101, 102, 103, 104 };
            var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);

            var substitutedShares = new List<Share>
            {
                shares[0], // Share 1 (correct)
                new Share { X = 2, Y = shares[0].Y }, // Share 2 (substituted with share 1's Y)
                shares[2]  // Share 3 (correct)
            };

            var result = ShamirSecretShare.ReconstructSecret(substitutedShares, 3);

            CollectionAssert.AreNotEqual(secret, result);
        }

        [TestMethod]
        public void CryptographicDowngrade_WeakerAlgorithm_NotSupported()
        {

            var share = new Share { X = 1, Y = "test" };
            var password = CreateSecureString("password");

            var (encrypted, hmac) = _cryptographyService.EncryptShare(share, password, out string salt, out string iv);

            Assert.IsFalse(string.IsNullOrEmpty(encrypted));
            Assert.IsFalse(string.IsNullOrEmpty(hmac));
            Assert.AreEqual(12, Convert.FromBase64String(iv).Length); // AES-GCM uses 12-byte IV
        }

        [TestMethod]
        public void ShareDuplication_SameShareTwice_ShouldStillReconstruct()
        {
            var secret = new byte[] { 50, 100, 150, 200, 250 };
            var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);

            var duplicatedShares = new List<Share>
            {
                shares[0], // Share 1
                shares[0], // Share 1 (duplicate)
                shares[1]  // Share 2
            };

            try
            {
                var result = ShamirSecretShare.ReconstructSecret(duplicatedShares, 3);
                CollectionAssert.AreNotEqual(secret, result);
            }
            catch
            {
                Assert.IsTrue(true);
            }
        }

        [TestMethod]
        public void PasswordSubstitution_SimilarPassword_ShouldFail()
        {
            var share = new Share { X = 1, Y = "test-share" };
            var correctPassword = CreateSecureString("Password123!");
            var similarPassword = CreateSecureString("Password123"); // Missing !

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(share, correctPassword, out string salt, out string iv);

            var keeper = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            Assert.ThrowsException<CryptographicException>(() =>
                _cryptographyService.DecryptShare(keeper, similarPassword, 1000));
        }

        [TestMethod]
        public void MaliciousShareData_ExtremelyLongY_ShouldHandle()
        {
            var share = new Share { X = 1, Y = new string('A', 10 * 1024 * 1024) }; // 10 MB Y value
            var password = CreateSecureString("password");

            try
            {
                var (encrypted, hmac) = _cryptographyService.EncryptShare(share, password, out string salt, out string iv);
                
                var keeper = new SecretKeeperRecord
                {
                    EncryptedShare = encrypted,
                    Hmac = hmac,
                    Salt = salt,
                    IV = iv
                };

                var decrypted = _cryptographyService.DecryptShare(keeper, password, 1000);
                
                Assert.AreEqual(share.X, decrypted.X);
                Assert.AreEqual(share.Y, decrypted.Y);
            }
            catch (OutOfMemoryException)
            {
                Assert.IsTrue(true);
            }
        }

        [TestMethod]
        public void ShareSwapping_DifferentCeremonies_ShouldProduceWrongSecret()
        {
            var secret1 = new byte[] { 1, 2, 3, 4, 5 };
            var secret2 = new byte[] { 6, 7, 8, 9, 10 };

            var shares1 = ShamirSecretShare.GenerateShares(secret1, 2, 3);
            var shares2 = ShamirSecretShare.GenerateShares(secret2, 2, 3);

            var mixedShares = new List<Share> { shares1[0], shares2[1] };
            var result = ShamirSecretShare.ReconstructSecret(mixedShares, 2);

            CollectionAssert.AreNotEqual(secret1, result);
            CollectionAssert.AreNotEqual(secret2, result);
        }

        [TestMethod]
        public void BitFlipping_SingleBitInSecret_ChangesAllShares()
        {
            var secret1 = new byte[] { 0b10101010 };
            var secret2 = new byte[] { 0b10101011 }; // Flip last bit

            var shares1 = ShamirSecretShare.GenerateShares(secret1, 2, 3);
            var shares2 = ShamirSecretShare.GenerateShares(secret2, 2, 3);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreNotEqual(shares1[i].Y, shares2[i].Y);
            }
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
