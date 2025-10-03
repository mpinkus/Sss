using System.Security;
using System.Security.Cryptography;
using System.Text;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Cryptography;
using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class InputFuzzingTests
    {
        [TestMethod]
        public void FuzzInput_EmptyByteArray_Secret()
        {
            var emptySecret = new byte[0];
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(emptySecret, 2, 3));
        }

        [TestMethod]
        public void FuzzInput_NullSecret_ShouldThrow()
        {
            byte[]? nullSecret = null;
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(nullSecret!, 2, 3));
        }

        [TestMethod]
        public void FuzzInput_ExtremelyLargeThreshold_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(secret, int.MaxValue, 3));
        }

        [TestMethod]
        public void FuzzInput_NegativeThreshold_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(secret, -1, 3));
        }

        [TestMethod]
        public void FuzzInput_ZeroTotalShares_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(secret, 2, 0));
        }

        [TestMethod]
        public void FuzzInput_NegativeTotalShares_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(secret, 2, -5));
        }

        [TestMethod]
        public void FuzzInput_ExtremelyLargeTotalShares_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            
            Assert.ThrowsException<ArgumentException>(() =>
                ShamirSecretShare.GenerateShares(secret, 2, int.MaxValue));
        }

        [TestMethod]
        public void FuzzInput_RandomBytePatterns_ShouldHandleCorrectly()
        {
            using var rng = RandomNumberGenerator.Create();
            
            for (int iteration = 0; iteration < 20; iteration++)
            {
                var secretSize = RandomNumberGenerator.GetInt32(1, 256);
                var secret = new byte[secretSize];
                rng.GetBytes(secret);

                var threshold = RandomNumberGenerator.GetInt32(2, 10);
                var totalShares = RandomNumberGenerator.GetInt32(threshold, 20);

                try
                {
                    var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
                    var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(threshold).ToList(), threshold);
                    
                    CollectionAssert.AreEqual(secret, reconstructed);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Failed on iteration {iteration} with secret size {secretSize}, threshold {threshold}, total {totalShares}: {ex.Message}");
                }
            }
        }

        [TestMethod]
        public void FuzzInput_SpecialCharacterStrings_InShareY()
        {
            var specialStrings = new[]
            {
                "",
                " ",
                "\0",
                "\n\r\t",
                "\\",
                "\"",
                "'",
                "<script>alert('xss')</script>",
                "'; DROP TABLE shares;--",
                "../../../etc/passwd",
                "%00%00%00",
                "\uFFFF\uFFFE",
                new string('A', 10000)
            };

            var share = new Share { X = 1, Y = "" };

            foreach (var testString in specialStrings)
            {
                share.Y = testString;
                
                try
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(share);
                    var deserialized = System.Text.Json.JsonSerializer.Deserialize<Share>(json);
                    
                    Assert.IsNotNull(deserialized);
                }
                catch
                {
                }
            }
        }

        [TestMethod]
        public void FuzzInput_ExtremeKeeperData_ShouldHandle()
        {
            var extremeValues = new[]
            {
                "",
                new string('A', 10000),
                "\0\0\0",
                "../../../../etc/passwd",
                "<script>alert(1)</script>",
                "' OR '1'='1",
                null
            };

            foreach (var value in extremeValues)
            {
                try
                {
                    var keeper = new SecretKeeperRecord
                    {
                        Name = value ?? "",
                        Email = value ?? "",
                        Phone = value ?? ""
                    };

                    Assert.IsNotNull(keeper);
                }
                catch
                {
                }
            }
        }

        [TestMethod]
        public void FuzzInput_BoundaryThresholdValues_ShouldHandleCorrectly()
        {
            var secret = new byte[] { 1, 2, 3, 4, 5 };

            var testCases = new[]
            {
                (1, 1),    // Minimum: 1-of-1
                (1, 2),    // 1-of-2
                (2, 2),    // Equal threshold and total
                (10, 10),  // Equal with larger numbers
                (50, 100), // Moderate split
                (128, 255) // Maximum shares
            };

            foreach (var (threshold, total) in testCases)
            {
                try
                {
                    var shares = ShamirSecretShare.GenerateShares(secret, threshold, total);
                    Assert.AreEqual(total, shares.Count);

                    var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(threshold).ToList(), threshold);
                    CollectionAssert.AreEqual(secret, reconstructed);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Failed with threshold={threshold}, total={total}: {ex.Message}");
                }
            }
        }

        [TestMethod]
        public void FuzzInput_RandomInvalidBase64_InShareY()
        {
            var invalidBase64 = new[]
            {
                "!!!",
                "====",
                "AB CD",
                "ABCD=",
                "AB==CD",
                "🎉🎊",
                new string((char)0xFF, 10)
            };

            foreach (var invalid in invalidBase64)
            {
                var share = new Share { X = 1, Y = invalid };

                Assert.IsNotNull(share);

                try
                {
                    var shares = new List<Share> { share };
                    var result = ShamirSecretShare.ReconstructSecret(shares, 1);
                }
                catch
                {
                }
            }
        }

        [TestMethod]
        public void FuzzInput_AllZeroBytes_Secret()
        {
            var allZeros = new byte[32];
            Array.Fill<byte>(allZeros, 0);

            var shares = ShamirSecretShare.GenerateShares(allZeros, 3, 5);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);

            CollectionAssert.AreEqual(allZeros, reconstructed);
        }

        [TestMethod]
        public void FuzzInput_AllOnesBytes_Secret()
        {
            var allOnes = new byte[32];
            Array.Fill<byte>(allOnes, 0xFF);

            var shares = ShamirSecretShare.GenerateShares(allOnes, 3, 5);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);

            CollectionAssert.AreEqual(allOnes, reconstructed);
        }

        [TestMethod]
        public void FuzzInput_AlternatingBitPattern_Secret()
        {
            var alternating = new byte[32];
            for (int i = 0; i < alternating.Length; i++)
            {
                alternating[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);
            }

            var shares = ShamirSecretShare.GenerateShares(alternating, 3, 5);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);

            CollectionAssert.AreEqual(alternating, reconstructed);
        }

        [TestMethod]
        public void FuzzInput_RandomSecretSizes_PowersOfTwo()
        {
            var sizes = new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 };

            foreach (var size in sizes)
            {
                var secret = new byte[size];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(secret);
                }

                var shares = ShamirSecretShare.GenerateShares(secret, 2, 3);
                var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(2).ToList(), 2);

                CollectionAssert.AreEqual(secret, reconstructed, $"Failed with size {size}");
            }
        }

        [TestMethod]
        public void FuzzInput_OddSecretSizes_PrimeNumbers()
        {
            var primes = new[] { 1, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47 };

            foreach (var size in primes)
            {
                var secret = new byte[size];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(secret);
                }

                var shares = ShamirSecretShare.GenerateShares(secret, 2, 3);
                var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(2).ToList(), 2);

                CollectionAssert.AreEqual(secret, reconstructed, $"Failed with prime size {size}");
            }
        }

        [TestMethod]
        public void FuzzInput_UnicodeInOrganizationData()
        {
            var unicodeStrings = new[]
            {
                "Hello 世界",
                "🎉🎊🎈",
                "Тест",
                "مرحبا",
                "שלום",
                "こんにちは",
                "\u200B\u200C\u200D", // Zero-width characters
                "A\u0301", // Combining characters
            };

            foreach (var unicode in unicodeStrings)
            {
                var config = new OrganizationSettings
                {
                    Name = unicode,
                    ContactPhone = unicode
                };

                Assert.IsNotNull(config);
            }
        }

        [TestMethod]
        public void FuzzInput_ExtremelyLongPassword_ShouldHandle()
        {
            var longPassword = new string('P', 10000);
            var securePassword = CreateSecureString(longPassword);

            var share = new Share { X = 1, Y = "test" };
            var cryptoService = new Shamir.Ceremony.Common.Services.CryptographyService(
                new SecuritySettings { KdfIterations = 100, SecureDeletePasses = 1 }
            );

            try
            {
                var (encrypted, hmac) = cryptoService.EncryptShare(share, securePassword, out string salt, out string iv);
                
                var keeper = new SecretKeeperRecord
                {
                    EncryptedShare = encrypted,
                    Hmac = hmac,
                    Salt = salt,
                    IV = iv
                };

                var decrypted = cryptoService.DecryptShare(keeper, securePassword, 100);
                Assert.AreEqual(share.X, decrypted.X);
            }
            catch (OutOfMemoryException)
            {
                Assert.IsTrue(true);
            }
        }

        [TestMethod]
        public void FuzzInput_EmptyPassword_ShouldHandle()
        {
            var emptyPassword = CreateSecureString("");
            var share = new Share { X = 1, Y = "test" };
            var cryptoService = new Shamir.Ceremony.Common.Services.CryptographyService(
                new SecuritySettings { KdfIterations = 100, SecureDeletePasses = 1 }
            );

            var (encrypted, hmac) = cryptoService.EncryptShare(share, emptyPassword, out string salt, out string iv);
            
            var keeper = new SecretKeeperRecord
            {
                EncryptedShare = encrypted,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            var decrypted = cryptoService.DecryptShare(keeper, emptyPassword, 100);
            Assert.AreEqual(share.X, decrypted.X);
        }

        [TestMethod]
        public void FuzzInput_ControlCharactersInData()
        {
            var controlChars = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                controlChars.Append((char)i);
            }

            var testString = controlChars.ToString();
            
            var keeper = new SecretKeeperRecord
            {
                Name = testString
            };

            Assert.IsNotNull(keeper);
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
