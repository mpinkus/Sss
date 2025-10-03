using System.Runtime.InteropServices;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Cryptography;

namespace Shamir.Ceremony.Common.Tests.Integration
{
    [TestClass]
    public sealed class CrossPlatformTests
    {
        [TestMethod]
        public void PlatformDetection_CorrectlyIdentifiesOS()
        {
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMacOS = OperatingSystem.IsMacOS();

            Assert.IsTrue(isWindows || isLinux || isMacOS, "Should identify at least one OS");

            Console.WriteLine($"Detected Platform: Windows={isWindows}, Linux={isLinux}, macOS={isMacOS}");
        }

        [TestMethod]
        public void FilePathHandling_CrossPlatform_ShouldWork()
        {
            var tempPath = Path.GetTempPath();
            Assert.IsFalse(string.IsNullOrEmpty(tempPath));

            var testPath = Path.Combine(tempPath, "shamir_test", "subfolder");
            
            Assert.IsTrue(Path.IsPathFullyQualified(tempPath));
            
            var separator = Path.DirectorySeparatorChar;
            Assert.IsTrue(separator == '/' || separator == '\\');
        }

        [TestMethod]
        public void LineEndings_CrossPlatform_ShouldBeNormalized()
        {
            var windowsStyle = "Line1\r\nLine2\r\nLine3";
            var unixStyle = "Line1\nLine2\nLine3";

            var windowsLines = windowsStyle.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var unixLines = unixStyle.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            Assert.AreEqual(3, windowsLines.Length);
            Assert.AreEqual(3, unixLines.Length);
        }

        [TestMethod]
        public void CryptographicOperations_CrossPlatform_ProduceSameResults()
        {
            var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var threshold = 3;
            var totalShares = 5;

            var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

            Assert.AreEqual(totalShares, shares.Count);

            var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(threshold).ToList(), threshold);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public void Configuration_DefaultPaths_ShouldBeValid()
        {
            var config = new CeremonyConfiguration
            {
                FileSystem = new FileSystemSettings
                {
                    OutputFolder = Path.Combine(Path.GetTempPath(), "ShamirTest")
                }
            };

            Assert.IsFalse(string.IsNullOrEmpty(config.FileSystem.OutputFolder));
            
            if (!Directory.Exists(config.FileSystem.OutputFolder))
            {
                Directory.CreateDirectory(config.FileSystem.OutputFolder);
            }

            Assert.IsTrue(Directory.Exists(config.FileSystem.OutputFolder));

            try
            {
                Directory.Delete(config.FileSystem.OutputFolder, true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void FileOperations_CrossPlatform_CreateReadDelete()
        {
            var testFolder = Path.Combine(Path.GetTempPath(), $"ShamirCrossPlatform_{Guid.NewGuid():N}");
            Directory.CreateDirectory(testFolder);

            try
            {
                var testFile = Path.Combine(testFolder, "test.json");
                var testContent = "{\"test\": \"data\"}";

                File.WriteAllText(testFile, testContent);
                Assert.IsTrue(File.Exists(testFile));

                var readContent = File.ReadAllText(testFile);
                Assert.AreEqual(testContent, readContent);

                File.Delete(testFile);
                Assert.IsFalse(File.Exists(testFile));
            }
            finally
            {
                if (Directory.Exists(testFolder))
                {
                    Directory.Delete(testFolder, true);
                }
            }
        }

        [TestMethod]
        public void CharacterEncoding_CrossPlatform_UTF8Handling()
        {
            var unicodeText = "Hello 世界 🌍 Тест مرحبا";
            var bytes = System.Text.Encoding.UTF8.GetBytes(unicodeText);
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.AreEqual(unicodeText, decoded);
        }

        [TestMethod]
        public void ProcessArchitecture_ShouldBeIdentifiable()
        {
            var architecture = RuntimeInformation.ProcessArchitecture;
            
            Assert.IsTrue(
                architecture == Architecture.X64 ||
                architecture == Architecture.X86 ||
                architecture == Architecture.Arm ||
                architecture == Architecture.Arm64,
                $"Architecture should be identifiable: {architecture}"
            );

            Console.WriteLine($"Process Architecture: {architecture}");
        }

        [TestMethod]
        public void MemoryOperations_CrossPlatform_ShouldWork()
        {
            var data = new byte[1024];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);

            Array.Clear(data, 0, data.Length);
            Assert.IsTrue(data.All(b => b == 0));

            var source = new byte[] { 1, 2, 3, 4, 5 };
            var dest = new byte[5];
            Buffer.BlockCopy(source, 0, dest, 0, 5);

            CollectionAssert.AreEqual(source, dest);
        }

        [TestMethod]
        public void DateTime_CrossPlatform_UTCHandling()
        {
            var utcNow = DateTime.UtcNow;
            var localNow = DateTime.Now;

            Assert.AreEqual(DateTimeKind.Utc, utcNow.Kind);
            Assert.AreEqual(DateTimeKind.Local, localNow.Kind);

            var converted = localNow.ToUniversalTime();
            Assert.AreEqual(DateTimeKind.Utc, converted.Kind);
        }

        [TestMethod]
        public void EnvironmentVariables_CrossPlatform_ShouldBeAccessible()
        {
            var tempVar = Environment.GetEnvironmentVariable("TEMP") ?? 
                         Environment.GetEnvironmentVariable("TMP") ?? 
                         Environment.GetEnvironmentVariable("TMPDIR");

            Assert.IsNotNull(tempVar, "Should find a temp directory environment variable");
        }

        [TestMethod]
        public void NetworkProtocols_CrossPlatform_ShouldBeAvailable()
        {
            try
            {
                var hostName = System.Net.Dns.GetHostName();
                Assert.IsFalse(string.IsNullOrEmpty(hostName));
            }
            catch (Exception ex)
            {
                Assert.Fail($"DNS lookup failed: {ex.Message}");
            }
        }

        [TestMethod]
        public void ThreadingOperations_CrossPlatform_ShouldWork()
        {
            var counter = 0;
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    Interlocked.Increment(ref counter);
                }));
            }

            Task.WaitAll(tasks.ToArray());

            Assert.AreEqual(10, counter);
        }

        [TestMethod]
        public void CryptographicRNG_CrossPlatform_ShouldWork()
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            
            var buffer1 = new byte[32];
            var buffer2 = new byte[32];

            rng.GetBytes(buffer1);
            rng.GetBytes(buffer2);

            CollectionAssert.AreNotEqual(buffer1, buffer2);
        }
    }
}
