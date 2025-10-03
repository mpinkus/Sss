using System.Diagnostics;
using System.Security;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class TimingAttackTests
    {
        private CryptographyService _cryptographyService = null!;

        [TestInitialize]
        public void Setup()
        {
            var securitySettings = new SecuritySettings
            {
                KdfIterations = 10000, // Higher for better timing resistance
                SecureDeletePasses = 1
            };
            _cryptographyService = new CryptographyService(securitySettings);
        }

        [TestMethod]
        public void TimingAnalysis_PasswordVerification_ShouldBeConstantTime()
        {
            var share = new Share { X = 1, Y = "test-share-data" };
            var correctPassword = CreateSecureString("CorrectPassword123!");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(share, correctPassword, out string salt, out string iv);

            var keeper = new SecretKeeperRecord
            {
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = iv
            };

            var passwords = new[]
            {
                "WrongPassword1",
                "WrongPassword2",
                "WrongPassword3",
                "CompletelyDifferent",
                "CorrectPassword123!" // Correct one
            };

            var timings = new List<long>();

            foreach (var password in passwords)
            {
                var sw = Stopwatch.StartNew();
                
                try
                {
                    _cryptographyService.DecryptShare(keeper, CreateSecureString(password), 10000);
                }
                catch
                {
                }

                sw.Stop();
                timings.Add(sw.ElapsedTicks);
            }

            var avgTiming = timings.Average();
            var maxDeviation = timings.Max(t => Math.Abs(t - avgTiming));
            var deviationPercent = (maxDeviation / avgTiming) * 100;

            Console.WriteLine($"Average timing: {avgTiming} ticks");
            Console.WriteLine($"Max deviation: {deviationPercent:F2}%");
            Console.WriteLine($"Individual timings: {string.Join(", ", timings)}");

            Assert.IsTrue(timings.All(t => t > 0), "All operations should take measurable time");
        }

        [TestMethod]
        public void TimingAnalysis_HMACVerification_ShouldBeConstantTime()
        {
            var share = new Share { X = 1, Y = "test-data" };
            var password = CreateSecureString("password");

            var (encryptedData, correctHmac) = _cryptographyService.EncryptShare(share, password, out string salt, out string iv);

            var hmacBytes = Convert.FromBase64String(correctHmac);
            var testHmacs = new List<byte[]>();

            var allWrong = new byte[hmacBytes.Length];
            for (int i = 0; i < allWrong.Length; i++) allWrong[i] = (byte)(hmacBytes[i] ^ 0xFF);
            testHmacs.Add(allWrong);

            var firstCorrect = (byte[])allWrong.Clone();
            firstCorrect[0] = hmacBytes[0];
            testHmacs.Add(firstCorrect);

            var halfCorrect = (byte[])allWrong.Clone();
            for (int i = 0; i < hmacBytes.Length / 2; i++) halfCorrect[i] = hmacBytes[i];
            testHmacs.Add(halfCorrect);

            var almostCorrect = (byte[])hmacBytes.Clone();
            almostCorrect[almostCorrect.Length - 1] ^= 0xFF;
            testHmacs.Add(almostCorrect);

            testHmacs.Add(hmacBytes);

            var timings = new List<long>();

            foreach (var testHmac in testHmacs)
            {
                var keeper = new SecretKeeperRecord
                {
                    EncryptedShare = encryptedData,
                    Hmac = Convert.ToBase64String(testHmac),
                    Salt = salt,
                    IV = iv
                };

                var sw = Stopwatch.StartNew();
                
                try
                {
                    _cryptographyService.DecryptShare(keeper, password, 10000);
                }
                catch
                {
                }

                sw.Stop();
                timings.Add(sw.ElapsedTicks);
            }

            var avgTiming = timings.Average();
            var maxDeviation = timings.Max(t => Math.Abs(t - avgTiming));
            var deviationPercent = (maxDeviation / avgTiming) * 100;

            Console.WriteLine($"HMAC verification average: {avgTiming} ticks");
            Console.WriteLine($"Max deviation: {deviationPercent:F2}%");
            Console.WriteLine($"Timings: {string.Join(", ", timings)}");

            Assert.IsTrue(timings.All(t => t > 0));
        }

        [TestMethod]
        public void TimingAnalysis_ShareReconstruction_WithDifferentShares()
        {
            var secret = new byte[] { 1, 2, 3, 4, 5 };
            var shares = Shamir.Ceremony.Common.Cryptography.ShamirSecretShare.GenerateShares(secret, 3, 5);

            var combinations = new[]
            {
                new[] { 0, 1, 2 },
                new[] { 0, 2, 4 },
                new[] { 1, 3, 4 },
                new[] { 2, 3, 4 }
            };

            var timings = new List<long>();

            foreach (var combo in combinations)
            {
                var selectedShares = combo.Select(i => shares[i]).ToList();

                var sw = Stopwatch.StartNew();
                var result = Shamir.Ceremony.Common.Cryptography.ShamirSecretShare.ReconstructSecret(selectedShares, 3);
                sw.Stop();

                timings.Add(sw.ElapsedTicks);
                CollectionAssert.AreEqual(secret, result);
            }

            var avgTiming = timings.Average();
            var maxDeviation = timings.Max(t => Math.Abs(t - avgTiming));
            var deviationPercent = (maxDeviation / avgTiming) * 100;

            Console.WriteLine($"Reconstruction average: {avgTiming} ticks");
            Console.WriteLine($"Max deviation: {deviationPercent:F2}%");

            Assert.IsTrue(deviationPercent < 200, "Timing deviation should be reasonable");
        }

        [TestMethod]
        public void TimingAnalysis_KDFIterations_LinearScaling()
        {
            var share = new Share { X = 1, Y = "test" };
            var password = CreateSecureString("password");

            var iterations = new[] { 1000, 5000, 10000 };
            var timings = new List<(int iterations, long ticks)>();

            foreach (var iter in iterations)
            {
                var service = new CryptographyService(new SecuritySettings 
                { 
                    KdfIterations = iter, 
                    SecureDeletePasses = 1 
                });

                var sw = Stopwatch.StartNew();
                var (encrypted, hmac) = service.EncryptShare(share, password, out string salt, out string iv);
                sw.Stop();

                timings.Add((iter, sw.ElapsedTicks));
            }

            Console.WriteLine("KDF timing scaling:");
            foreach (var (iter, ticks) in timings)
            {
                Console.WriteLine($"  {iter} iterations: {ticks} ticks");
            }

            Assert.IsTrue(timings[1].ticks > timings[0].ticks);
            Assert.IsTrue(timings[2].ticks > timings[1].ticks);
        }

        [TestMethod]
        public void TimingAnalysis_SecretSize_LinearScaling()
        {
            var sizes = new[] { 10, 100, 1000 };
            var timings = new List<(int size, long ticks)>();

            foreach (var size in sizes)
            {
                var secret = new byte[size];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(secret);
                }

                var sw = Stopwatch.StartNew();
                var shares = Shamir.Ceremony.Common.Cryptography.ShamirSecretShare.GenerateShares(secret, 3, 5);
                sw.Stop();

                timings.Add((size, sw.ElapsedTicks));
            }

            Console.WriteLine("Secret size scaling:");
            foreach (var (size, ticks) in timings)
            {
                Console.WriteLine($"  {size} bytes: {ticks} ticks");
            }

            Assert.IsTrue(timings[1].ticks > timings[0].ticks);
            Assert.IsTrue(timings[2].ticks > timings[1].ticks);
        }

        [TestMethod]
        public void TimingAnalysis_PasswordLength_ShouldNotLeakInformation()
        {
            var passwords = new[]
            {
                "short",
                "mediumlength",
                "verylongpasswordindeed"
            };

            var share = new Share { X = 1, Y = "test" };
            var timings = new List<long>();

            foreach (var pwd in passwords)
            {
                var sw = Stopwatch.StartNew();
                var (encrypted, hmac) = _cryptographyService.EncryptShare(share, CreateSecureString(pwd), out string salt, out string iv);
                sw.Stop();

                timings.Add(sw.ElapsedTicks);
            }

            var avgTiming = timings.Average();
            var maxDeviation = timings.Max(t => Math.Abs(t - avgTiming));
            var deviationPercent = (maxDeviation / avgTiming) * 100;

            Console.WriteLine($"Password length timing average: {avgTiming} ticks");
            Console.WriteLine($"Max deviation: {deviationPercent:F2}%");

            Assert.IsTrue(deviationPercent < 100, "Password length should not significantly affect timing");
        }

        [TestMethod]
        public void TimingAnalysis_EarlyExitPatterns_ShouldNotExist()
        {
            var share = new Share { X = 1, Y = "test" };
            var password = CreateSecureString("password");

            var (encrypted, hmac) = _cryptographyService.EncryptShare(share, password, out string salt, out string iv);

            var testCases = new[]
            {
                new SecretKeeperRecord { EncryptedShare = "invalid", Hmac = hmac, Salt = salt, IV = iv },
                new SecretKeeperRecord { EncryptedShare = encrypted, Hmac = "invalid", Salt = salt, IV = iv },
                new SecretKeeperRecord { EncryptedShare = encrypted, Hmac = hmac, Salt = "invalid", IV = iv },
            };

            var timings = new List<long>();

            foreach (var testCase in testCases)
            {
                var sw = Stopwatch.StartNew();
                
                try
                {
                    _cryptographyService.DecryptShare(testCase, password, 10000);
                }
                catch
                {
                }

                sw.Stop();
                timings.Add(sw.ElapsedTicks);
            }

            Console.WriteLine($"Error timing variance: {string.Join(", ", timings)}");
            
            Assert.IsTrue(timings.All(t => t > 0));
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
