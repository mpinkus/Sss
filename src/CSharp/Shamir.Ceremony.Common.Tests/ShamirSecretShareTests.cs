using Shamir.Ceremony.Common.Cryptography;
using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class ShamirSecretShareTests
    {
        [TestMethod]
        public void GenerateShares_WithValidParameters_ShouldSucceed()
        {
            var secret = new byte[] { 1, 2, 3, 4, 5 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            Assert.AreEqual(totalShares, shares.Count);
            for (int i = 0; i < totalShares; i++)
            {
                Assert.AreEqual(i + 1, shares[i].X);
                Assert.IsFalse(string.IsNullOrEmpty(shares[i].Y));
            }
        }

        [TestMethod]
        public void GenerateShares_WithThresholdEqualsTotal_ShouldSucceed()
        {
            var secret = new byte[] { 10, 20, 30 };
            int threshold = 3;
            int totalShares = 3;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            Assert.AreEqual(totalShares, shares.Count);
            Assert.IsTrue(shares.All(s => !string.IsNullOrEmpty(s.Y)));
        }

        [TestMethod]
        public void GenerateShares_WithThresholdOne_ShouldGenerateValidShares()
        {
            var secret = new byte[] { 42 };
            int threshold = 1;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            Assert.AreEqual(totalShares, shares.Count);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(1).ToList(), threshold);
            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void GenerateShares_WithMaxShares255_ShouldSucceed()
        {
            var secret = new byte[] { 1, 2, 3 };
            int threshold = 128;
            int totalShares = 255;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            Assert.AreEqual(totalShares, shares.Count);
            Assert.IsTrue(shares.Select(s => s.X).Distinct().Count() == totalShares);
        }

        [TestMethod]
        public void GenerateShares_WithVariousSecretSizes_ShouldSucceed()
        {
            var testCases = new[]
            {
                1,      // 1 byte
                16,     // 16 bytes
                32,     // 32 bytes
                64,     // 64 bytes
                256,    // 256 bytes
                1024    // 1 KB
            };

            foreach (var size in testCases)
            {
                var secret = new byte[size];
                for (int i = 0; i < size; i++)
                    secret[i] = (byte)(i % 256);

                var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);
                Assert.AreEqual(5, shares.Count);

                var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);
                CollectionAssert.AreEqual(secret, reconstructed);
            }
        }

        [TestMethod]
        public void GenerateShares_WithLargeSecret_1MB_ShouldSucceed()
        {
            var secret = new byte[1024 * 1024]; // 1 MB
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(secret);
            }

            var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);
            
            Assert.AreEqual(5, shares.Count);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);
            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateShares_WithEmptySecret_ShouldThrow()
        {
            var secret = new byte[0];
            ShamirSecretShare.GenerateShares(secret, 2, 3);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateShares_WithThresholdGreaterThanTotal_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            ShamirSecretShare.GenerateShares(secret, 5, 3);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateShares_WithZeroThreshold_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            ShamirSecretShare.GenerateShares(secret, 0, 3);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateShares_WithNegativeThreshold_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            ShamirSecretShare.GenerateShares(secret, -1, 3);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateShares_WithZeroTotalShares_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            ShamirSecretShare.GenerateShares(secret, 2, 0);
        }

        [TestMethod]
        public void ReconstructSecret_WithExactThreshold_ShouldMatch()
        {
            var secret = new byte[] { 10, 20, 30, 40, 50 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(threshold).ToList(), threshold);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void ReconstructSecret_WithMoreThanThreshold_ShouldMatch()
        {
            var secret = new byte[] { 10, 20, 30, 40, 50 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(4).ToList(), threshold);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void ReconstructSecret_WithAllShares_ShouldMatch()
        {
            var secret = new byte[] { 100, 101, 102, 103, 104 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares, threshold);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void ReconstructSecret_WithDifferentShareCombinations_ShouldAllMatch()
        {
            var secret = new byte[] { 1, 2, 3, 4, 5 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            var combinations = new[]
            {
                new[] { 0, 1, 2 },
                new[] { 0, 2, 4 },
                new[] { 1, 3, 4 },
                new[] { 0, 1, 3 },
                new[] { 2, 3, 4 }
            };

            foreach (var combo in combinations)
            {
                var selectedShares = combo.Select(i => shares[i]).ToList();
                var reconstructed = ShamirSecretShare.ReconstructSecret(selectedShares, threshold);
                CollectionAssert.AreEqual(secret, reconstructed, 
                    $"Failed with combination: {string.Join(",", combo)}");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ReconstructSecret_WithInsufficientShares_ShouldThrow()
        {
            var secret = new byte[] { 1, 2, 3 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            ShamirSecretShare.ReconstructSecret(shares.Take(2).ToList(), threshold);
        }

        [TestMethod]
        public void ReconstructSecret_WithCorruptedShare_ShouldNotMatchOriginal()
        {
            var secret = new byte[] { 10, 20, 30 };
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            
            var corruptedShares = shares.Take(3).ToList();
            var originalY = Convert.FromBase64String(corruptedShares[1].Y);
            originalY[0] ^= 0xFF; // Flip bits
            corruptedShares[1] = new Share { X = corruptedShares[1].X, Y = Convert.ToBase64String(originalY) };

            var reconstructed = ShamirSecretShare.ReconstructSecret(corruptedShares, threshold);

            CollectionAssert.AreNotEqual(secret, reconstructed);
        }

        [TestMethod]
        public void GenerateShares_DifferentSecretsWithSameParameters_ShouldProduceDifferentShares()
        {
            var secret1 = new byte[] { 1, 2, 3 };
            var secret2 = new byte[] { 4, 5, 6 };

            var shares1 = ShamirSecretShare.GenerateShares(secret1, 3, 5);
            var shares2 = ShamirSecretShare.GenerateShares(secret2, 3, 5);

            for (int i = 0; i < 5; i++)
            {
                Assert.AreNotEqual(shares1[i].Y, shares2[i].Y);
            }
        }

        [TestMethod]
        public void GenerateShares_SameSecretMultipleTimes_ShouldProduceDifferentShares()
        {
            var secret = new byte[] { 1, 2, 3 };

            var shares1 = ShamirSecretShare.GenerateShares(secret, 3, 5);
            var shares2 = ShamirSecretShare.GenerateShares(secret, 3, 5);

            bool anyDifferent = false;
            for (int i = 0; i < 5; i++)
            {
                if (shares1[i].Y != shares2[i].Y)
                {
                    anyDifferent = true;
                    break;
                }
            }
            Assert.IsTrue(anyDifferent, "Multiple generations with same secret should produce different shares");
        }

        [TestMethod]
        public void ReconstructSecret_WithMaxComplexity_ShouldSucceed()
        {
            var secret = new byte[128];
            for (int i = 0; i < 128; i++)
                secret[i] = (byte)(i % 256);

            int threshold = 128;
            int totalShares = 255;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(threshold).ToList(), threshold);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void GenerateShares_WithUnicodeBytes_ShouldHandleCorrectly()
        {
            var secret = System.Text.Encoding.UTF8.GetBytes("Hello 世界 🌍 Тест");
            int threshold = 3;
            int totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), threshold);

            CollectionAssert.AreEqual(secret, reconstructed);
            var reconstructedText = System.Text.Encoding.UTF8.GetString(reconstructed);
            Assert.AreEqual("Hello 世界 🌍 Тест", reconstructedText);
        }

        [TestMethod]
        public void GenerateShares_WithAllZeroBytes_ShouldSucceed()
        {
            var secret = new byte[32];
            Array.Fill<byte>(secret, 0);

            var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void GenerateShares_WithAllOnesBytes_ShouldSucceed()
        {
            var secret = new byte[32];
            Array.Fill<byte>(secret, 0xFF);

            var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);
            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void GenerateShares_WithRandomPatterns_ShouldAlwaysReconstructCorrectly()
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var secret = new byte[64];
                rng.GetBytes(secret);

                var shares = ShamirSecretShare.GenerateShares(secret, 5, 10);
                var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(5).ToList(), 5);

                CollectionAssert.AreEqual(secret, reconstructed, $"Failed on iteration {iteration}");
            }
        }
    }
}
