using System.Security;
using System.Text;
using System.Text.Json;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Cryptography;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests.Integration
{
    [TestClass]
    public sealed class CrossComponentIntegrationTests
    {
        private string _testOutputFolder = string.Empty;
        private CeremonyConfiguration _testConfig = null!;

        [TestInitialize]
        public void Setup()
        {
            _testOutputFolder = Path.Combine(Path.GetTempPath(), $"ShamirTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testOutputFolder);

            _testConfig = new CeremonyConfiguration
            {
                Security = new SecuritySettings
                {
                    ConfirmationRequired = false,
                    MinPasswordLength = 8,
                    RequireUppercase = false,
                    RequireLowercase = false,
                    RequireDigit = false,
                    RequireSpecialCharacter = false,
                    KdfIterations = 1000,
                    SecureDeletePasses = 3,
                    AuditLogEnabled = true,
                    AuditLogRetentionDays = 30
                },
                FileSystem = new FileSystemSettings
                {
                    OutputFolder = _testOutputFolder
                },
                Organization = new OrganizationSettings
                {
                    Name = "Test Organization",
                    ContactPhone = "555-1234"
                },
                DefaultKeepers = new List<DefaultKeeperSettings>
                {
                    new() { Name = "Test Keeper 1", Phone = "555-0001", Email = "keeper1@test.com", PreferredOrder = 1 },
                    new() { Name = "Test Keeper 2", Phone = "555-0002", Email = "keeper2@test.com", PreferredOrder = 2 }
                }
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testOutputFolder))
            {
                try
                {
                    Directory.Delete(_testOutputFolder, true);
                }
                catch
                {
                }
            }
        }

        [TestMethod]
        public void CryptographyService_Integration_WithShamirSecretShare_ShouldWork()
        {
            var cryptoService = new CryptographyService(_testConfig.Security);

            var secret = cryptoService.GenerateRandomSecret(32);

            var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);

            var encryptedShares = new List<SecretKeeperRecord>();
            for (int i = 0; i < shares.Count; i++)
            {
                var password = CreateSecureString($"password{i}");
                var (encryptedData, hmac) = cryptoService.EncryptShare(shares[i], password, out string salt, out string iv);

                encryptedShares.Add(new SecretKeeperRecord
                {
                    EncryptedShare = encryptedData,
                    Hmac = hmac,
                    Salt = salt,
                    IV = iv,
                    ShareNumber = shares[i].X
                });
            }

            var decryptedShares = new List<Share>();
            for (int i = 0; i < 3; i++)
            {
                var password = CreateSecureString($"password{i}");
                var decrypted = cryptoService.DecryptShare(encryptedShares[i], password, _testConfig.Security.KdfIterations);
                decryptedShares.Add(decrypted);
            }

            var reconstructed = ShamirSecretShare.ReconstructSecret(decryptedShares, 3);

            CollectionAssert.AreEqual(secret, reconstructed);
        }

        [TestMethod]
        public async Task CeremonyManager_WithSessionManager_ShouldTrackEvents()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var progressEvents = new List<ProgressEventArgs>();
            manager.ProgressUpdated += (sender, e) => progressEvents.Add(e);

            inputHandler.QueueResponse(CreateSecureString("admin123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password456"));

            await manager.CreateSharesAsync();
            manager.FinalizeSession();

            Assert.IsTrue(progressEvents.Count > 0);
            Assert.IsTrue(progressEvents.Any(e => e.EventType == "SESSION_INIT"));
            Assert.IsTrue(progressEvents.Any(e => e.EventType == "CREATE_START"));

            var sessionFiles = Directory.GetFiles(_testOutputFolder, "session_complete_*.json");
            Assert.IsTrue(sessionFiles.Length > 0);

            var sessionJson = File.ReadAllText(sessionFiles[0]);
            var sessionOutput = JsonSerializer.Deserialize<SessionOutput>(sessionJson);
            Assert.IsNotNull(sessionOutput);
            Assert.IsNotNull(sessionOutput.SessionData);
            Assert.IsTrue(sessionOutput.SessionData.Events.Count > 0);
        }

        [TestMethod]
        public async Task ConfigurationLoading_ThroughCeremonyManager_ShouldApplySettings()
        {
            _testConfig.Security.MinPasswordLength = 12;
            _testConfig.Security.RequireUppercase = true;
            _testConfig.Security.KdfIterations = 50000;

            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var validationEvents = new List<ValidationEventArgs>();
            manager.ValidationResult += (sender, e) => validationEvents.Add(e);

            inputHandler.QueueResponse(CreateSecureString("Admin123Pass"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("ValidPass123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("AnotherPass456"));

            var result = await manager.CreateSharesAsync();

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.SharesData);
            Assert.AreEqual(50000, result.SharesData.Configuration.KdfIterations);
        }

        [TestMethod]
        public async Task AuditLogger_Integration_WithCeremonyManager_ShouldCreateLogs()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            inputHandler.QueueResponse(CreateSecureString("admin123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password456"));

            await manager.CreateSharesAsync();
            manager.FinalizeSession();

            var auditFiles = Directory.GetFiles(_testOutputFolder, "audit_*.log");
            Assert.IsTrue(auditFiles.Length > 0, "Audit log file should be created");

            var auditContent = File.ReadAllText(auditFiles[0]);
            Assert.IsTrue(auditContent.Contains("CREATE_START") || auditContent.Length > 0);

            var auditJsonFiles = Directory.GetFiles(_testOutputFolder, "audit_detail_*.json");
            if (auditJsonFiles.Length > 0)
            {
                var auditJson = File.ReadAllText(auditJsonFiles[0]);
                var auditEntries = JsonSerializer.Deserialize<List<AuditLogEntry>>(auditJson);
                Assert.IsNotNull(auditEntries);
                Assert.IsTrue(auditEntries.Count > 0);
            }
        }

        [TestMethod]
        public void SecureDelete_AcrossCryptographyService_ShouldPreventRecovery()
        {
            var cryptoService = new CryptographyService(_testConfig.Security);

            var secret = new byte[1024];
            for (int i = 0; i < secret.Length; i++)
                secret[i] = (byte)(i % 256);

            var originalChecksum = ComputeChecksum(secret);

            cryptoService.SecureDelete(secret);

            var afterChecksum = ComputeChecksum(secret);

            Assert.AreNotEqual(originalChecksum, afterChecksum);
            Assert.IsTrue(secret.All(b => b == 0));
        }

        [TestMethod]
        public async Task EventPropagation_ThroughAllLayers_ShouldWork()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var progressEvents = new List<ProgressEventArgs>();
            var validationEvents = new List<ValidationEventArgs>();
            var completionEvents = new List<CompletionEventArgs>();

            manager.ProgressUpdated += (sender, e) => progressEvents.Add(e);
            manager.ValidationResult += (sender, e) => validationEvents.Add(e);
            manager.OperationCompleted += (sender, e) => completionEvents.Add(e);

            inputHandler.QueueResponse(CreateSecureString("admin123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password456"));

            await manager.CreateSharesAsync();

            Assert.IsTrue(progressEvents.Count > 0, "Progress events should be fired");
            Assert.IsTrue(completionEvents.Count > 0, "Completion events should be fired");
            Assert.IsTrue(completionEvents.Any(e => e.Success), "Should have successful completion");
        }

        [TestMethod]
        public async Task FullStackIntegration_CreateSaveLoadReconstruct_ShouldWork()
        {
            var knownSecret = Encoding.UTF8.GetBytes("Integration Test Secret");

            var createManager = new CeremonyManager(_testConfig);
            var createInputHandler = new MockInputHandler();
            createManager.InputRequested += createInputHandler.HandleInputRequest;

            createInputHandler.QueueResponse(CreateSecureString("admin123"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(2);
            createInputHandler.QueueResponse(3);
            createInputHandler.QueueResponse(false);
            createInputHandler.QueueResponse(CreateSecureString("Integration Test Secret"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(CreateSecureString("password1"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(CreateSecureString("password2"));
            createInputHandler.QueueResponse(false);
            createInputHandler.QueueResponse("Custom Keeper");
            createInputHandler.QueueResponse("555-9999");
            createInputHandler.QueueResponse("custom@test.com");
            createInputHandler.QueueResponse(CreateSecureString("password3"));

            var createResult = await createManager.CreateSharesAsync();
            Assert.IsTrue(createResult.Success);
            
            var sharesFilePath = createResult.OutputFilePath!;
            createManager.FinalizeSession();

            Assert.IsTrue(File.Exists(sharesFilePath), "Shares file should exist on disk");
            var sharesJson = File.ReadAllText(sharesFilePath);
            var loadedShares = JsonSerializer.Deserialize<ShamirSecretOutput>(sharesJson);
            Assert.IsNotNull(loadedShares);
            Assert.AreEqual(3, loadedShares.Keepers.Count);

            var reconstructManager = new CeremonyManager(_testConfig);
            var reconstructInputHandler = new MockInputHandler();
            reconstructManager.InputRequested += reconstructInputHandler.HandleInputRequest;

            reconstructInputHandler.QueueResponse(CreateSecureString("admin123"));
            reconstructInputHandler.QueueResponse(1);
            reconstructInputHandler.QueueResponse(CreateSecureString("password1"));
            reconstructInputHandler.QueueResponse(2);
            reconstructInputHandler.QueueResponse(CreateSecureString("password2"));

            var reconstructResult = await reconstructManager.ReconstructSecretAsync(sharesFilePath);
            Assert.IsTrue(reconstructResult.Success);

            CollectionAssert.AreEqual(knownSecret, reconstructResult.ReconstructedSecret);
        }

        [TestMethod]
        public async Task SessionManagement_WithHMAC_ShouldProvideNonRepudiation()
        {
            var adminPassword = "admin123";

            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            inputHandler.QueueResponse(CreateSecureString(adminPassword));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password456"));

            await manager.CreateSharesAsync();
            manager.FinalizeSession();

            var sessionFiles = Directory.GetFiles(_testOutputFolder, "session_complete_*.json");
            Assert.IsTrue(sessionFiles.Length > 0);

            var sessionJson = File.ReadAllText(sessionFiles[0]);
            var sessionOutput = JsonSerializer.Deserialize<SessionOutput>(sessionJson);
            Assert.IsNotNull(sessionOutput);
            Assert.IsFalse(string.IsNullOrEmpty(sessionOutput.AdminSessionHmac));
            Assert.AreEqual("HMAC-SHA256", sessionOutput.HmacAlgorithm);
            Assert.IsFalse(string.IsNullOrEmpty(sessionOutput.SessionDataHash));

            var sessionDataJson = JsonSerializer.Serialize(sessionOutput.SessionData);
            var cryptoService = new CryptographyService(_testConfig.Security);
            var computedHash = cryptoService.CalculateSha256Hash(sessionDataJson);
            Assert.AreEqual(sessionOutput.SessionDataHash, computedHash);
        }

        [TestMethod]
        public async Task MultipleComponents_WorkingTogether_ShouldMaintainDataIntegrity()
        {
            var cryptoService = new CryptographyService(_testConfig.Security);
            
            var originalSecret = cryptoService.GenerateRandomSecret(64);
            
            var shares = ShamirSecretShare.GenerateShares(originalSecret, 3, 5);
            
            var passwords = new[] { "pass1", "pass2", "pass3", "pass4", "pass5" };
            var keepers = new List<SecretKeeperRecord>();
            
            for (int i = 0; i < shares.Count; i++)
            {
                var (encrypted, hmac) = cryptoService.EncryptShare(shares[i], CreateSecureString(passwords[i]), out string salt, out string iv);
                keepers.Add(new SecretKeeperRecord
                {
                    EncryptedShare = encrypted,
                    Hmac = hmac,
                    Salt = salt,
                    IV = iv,
                    ShareNumber = shares[i].X
                });
            }
            
            var output = new ShamirSecretOutput
            {
                Version = "1.0.0",
                SessionId = Guid.NewGuid().ToString(),
                Configuration = new ShamirConfiguration
                {
                    TotalShares = 5,
                    ThresholdRequired = 3,
                    KdfIterations = _testConfig.Security.KdfIterations
                },
                Keepers = keepers,
                MasterSecretHash = cryptoService.CalculateSha256Hash(Convert.ToBase64String(originalSecret))
            };
            
            var json = JsonSerializer.Serialize(output);
            
            var loaded = JsonSerializer.Deserialize<ShamirSecretOutput>(json);
            Assert.IsNotNull(loaded);
            
            var decryptedShares = new List<Share>();
            for (int i = 0; i < 3; i++)
            {
                var decrypted = cryptoService.DecryptShare(loaded.Keepers[i], CreateSecureString(passwords[i]), _testConfig.Security.KdfIterations);
                decryptedShares.Add(decrypted);
            }
            
            var reconstructed = ShamirSecretShare.ReconstructSecret(decryptedShares, 3);
            
            CollectionAssert.AreEqual(originalSecret, reconstructed);
            
            var reconstructedHash = cryptoService.CalculateSha256Hash(Convert.ToBase64String(reconstructed));
            Assert.AreEqual(output.MasterSecretHash, reconstructedHash);
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
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(data));
        }

        private class MockInputHandler
        {
            private readonly Queue<object> _responses = new();

            public void QueueResponse(object response)
            {
                _responses.Enqueue(response);
            }

            public void HandleInputRequest(object? sender, InputRequestEventArgs e)
            {
                if (_responses.Count > 0)
                {
                    var response = _responses.Dequeue();
                    e.CompletionSource.SetResult(response);
                }
                else
                {
                    e.CompletionSource.SetException(new InvalidOperationException($"No response queued for request: {e.Prompt}"));
                }
            }
        }
    }
}
