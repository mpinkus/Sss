using System.Security;
using System.Text;
using System.Text.Json;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests.Integration
{
    [TestClass]
    public sealed class CompleteCeremonyWorkflowTests
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
                    SecureDeletePasses = 1,
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
                    new() { Name = "Test Keeper 2", Phone = "555-0002", Email = "keeper2@test.com", PreferredOrder = 2 },
                    new() { Name = "Test Keeper 3", Phone = "555-0003", Email = "keeper3@test.com", PreferredOrder = 3 }
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
        public async Task CompleteWorkflow_CreateAndReconstruct_ShouldSucceed()
        {
            var knownSecret = "My Test Secret 123";
            var secretBytes = Encoding.UTF8.GetBytes(knownSecret);

            var createManager = new CeremonyManager(_testConfig);
            var createInputHandler = new MockInputHandler();
            createManager.InputRequested += createInputHandler.HandleInputRequest;

            createInputHandler.QueueResponse(CreateSecureString("admin123"));
            createInputHandler.QueueResponse(true); // Use default organization
            createInputHandler.QueueResponse(2); // Threshold
            createInputHandler.QueueResponse(3); // Total shares
            createInputHandler.QueueResponse(false); // Don't generate random
            createInputHandler.QueueResponse(CreateSecureString(knownSecret)); // User provided secret
            createInputHandler.QueueResponse(true); // Use default keeper 1
            createInputHandler.QueueResponse(CreateSecureString("password123"));
            createInputHandler.QueueResponse(true); // Use default keeper 2
            createInputHandler.QueueResponse(CreateSecureString("password456"));
            createInputHandler.QueueResponse(true); // Use default keeper 3
            createInputHandler.QueueResponse(CreateSecureString("password789"));

            var createResult = await createManager.CreateSharesAsync();

            Assert.IsTrue(createResult.Success, $"Create failed: {createResult.Message}");
            Assert.IsNotNull(createResult.OutputFilePath);
            Assert.IsTrue(File.Exists(createResult.OutputFilePath), "Shares file should exist");

            var allFiles = Directory.GetFiles(_testOutputFolder, "*.*", SearchOption.AllDirectories);
            Assert.IsTrue(allFiles.Length > 0, "Output files should be created");

            var sharesJson = File.ReadAllText(createResult.OutputFilePath);
            var sharesData = JsonSerializer.Deserialize<ShamirSecretOutput>(sharesJson);
            Assert.IsNotNull(sharesData);
            Assert.AreEqual(3, sharesData.Keepers.Count);
            Assert.AreEqual(2, sharesData.Configuration.ThresholdRequired);
            Assert.AreEqual(3, sharesData.Configuration.TotalShares);

            createManager.FinalizeSession();
            var sessionFiles = Directory.GetFiles(_testOutputFolder, "session_complete_*.json");
            Assert.IsTrue(sessionFiles.Length > 0, "Session file should be created");

            var readmeFiles = Directory.GetFiles(_testOutputFolder, "README.txt");
            Assert.IsTrue(readmeFiles.Length > 0, "README file should be created");

            var reconstructManager = new CeremonyManager(_testConfig);
            var reconstructInputHandler = new MockInputHandler();
            reconstructManager.InputRequested += reconstructInputHandler.HandleInputRequest;

            reconstructInputHandler.QueueResponse(CreateSecureString("admin123"));
            reconstructInputHandler.QueueResponse(1); // Select keeper 1
            reconstructInputHandler.QueueResponse(CreateSecureString("password123"));
            reconstructInputHandler.QueueResponse(2); // Select keeper 2
            reconstructInputHandler.QueueResponse(CreateSecureString("password456"));

            var reconstructResult = await reconstructManager.ReconstructSecretAsync(createResult.OutputFilePath);

            Assert.IsTrue(reconstructResult.Success, $"Reconstruct failed: {reconstructResult.Message}");
            Assert.IsNotNull(reconstructResult.ReconstructedSecret);
            var reconstructedText = Encoding.UTF8.GetString(reconstructResult.ReconstructedSecret);
            Assert.AreEqual(knownSecret, reconstructedText);

            var auditFiles = Directory.GetFiles(_testOutputFolder, "audit_*.log");
            Assert.IsTrue(auditFiles.Length > 0, "Audit log files should exist");

            var sessionJson = File.ReadAllText(sessionFiles[0]);
            var sessionOutput = JsonSerializer.Deserialize<SessionOutput>(sessionJson);
            Assert.IsNotNull(sessionOutput);
            Assert.IsFalse(string.IsNullOrEmpty(sessionOutput.AdminSessionHmac));
            Assert.IsFalse(string.IsNullOrEmpty(sessionOutput.SessionDataHash));
        }

        [TestMethod]
        public async Task ConfirmationTest_WhenEnabled_ShouldValidateShares()
        {
            _testConfig.Security.ConfirmationRequired = true;

            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            inputHandler.QueueResponse(CreateSecureString("admin123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(3);
            inputHandler.QueueResponse(true); // Generate random
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password456"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password789"));

            var result = await manager.CreateSharesAsync();

            Assert.IsTrue(result.Success, "Confirmation test should pass");
        }

        [TestMethod]
        public async Task MultipleSequentialCeremonies_ShouldIsolateSession()
        {
            var sessionIds = new List<string>();

            for (int i = 0; i < 3; i++)
            {
                var manager = new CeremonyManager(_testConfig);
                var inputHandler = new MockInputHandler();
                manager.InputRequested += inputHandler.HandleInputRequest;

                inputHandler.QueueResponse(CreateSecureString($"admin{i}"));
                inputHandler.QueueResponse(true);
                inputHandler.QueueResponse(2);
                inputHandler.QueueResponse(2);
                inputHandler.QueueResponse(true);
                inputHandler.QueueResponse(true);
                inputHandler.QueueResponse(CreateSecureString($"pass{i}"));
                inputHandler.QueueResponse(true);
                inputHandler.QueueResponse(CreateSecureString($"pass{i}"));

                var result = await manager.CreateSharesAsync();
                Assert.IsTrue(result.Success);
                
                manager.FinalizeSession();

                var sharesJson = File.ReadAllText(result.OutputFilePath!);
                var sharesData = JsonSerializer.Deserialize<ShamirSecretOutput>(sharesJson);
                sessionIds.Add(sharesData!.SessionId);
            }

            Assert.AreEqual(3, sessionIds.Distinct().Count(), "All sessions should have unique IDs");
        }

        [TestMethod]
        public async Task LargeSecret_10MB_ShouldCompleteSuccessfully()
        {
            var largeSecret = new byte[10 * 1024 * 1024]; // 10 MB
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(largeSecret);
            }

            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            inputHandler.QueueResponse(CreateSecureString("admin123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(3);
            inputHandler.QueueResponse(false);
            inputHandler.QueueResponse(CreateSecureString(Convert.ToBase64String(largeSecret)));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password123"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password456"));
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("password789"));

            var result = await manager.CreateSharesAsync();

            Assert.IsTrue(result.Success, "Large secret ceremony should succeed");
            Assert.IsTrue(File.Exists(result.OutputFilePath));
        }

        [TestMethod]
        public async Task PasswordPolicyEnforcement_ShouldValidatePasswords()
        {
            _testConfig.Security.MinPasswordLength = 12;
            _testConfig.Security.RequireUppercase = true;
            _testConfig.Security.RequireLowercase = true;
            _testConfig.Security.RequireDigit = true;
            _testConfig.Security.RequireSpecialCharacter = true;

            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var validationEvents = new List<ValidationEventArgs>();
            manager.ValidationResult += (sender, e) => validationEvents.Add(e);

            inputHandler.QueueResponse(CreateSecureString("Admin123!")); // Valid admin password
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(2);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("ValidPass123!")); // Valid
            inputHandler.QueueResponse(true);
            inputHandler.QueueResponse(CreateSecureString("AnotherPass456!")); // Valid

            var result = await manager.CreateSharesAsync();

            Assert.IsTrue(result.Success);
        }

        [TestMethod]
        public async Task AllFilesVerification_ShouldContainExpectedFiles()
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

            var allFiles = Directory.GetFiles(_testOutputFolder, "*.*", SearchOption.AllDirectories);
            var fileNames = allFiles.Select(Path.GetFileName).ToList();

            Assert.IsTrue(fileNames.Any(f => f!.StartsWith("secret_shares_")), "Should have shares file");
            Assert.IsTrue(fileNames.Any(f => f!.StartsWith("session_complete_")), "Should have session file");
            Assert.IsTrue(fileNames.Any(f => f == "README.txt"), "Should have README file");
            Assert.IsTrue(fileNames.Any(f => f!.StartsWith("audit_")), "Should have audit log file");
        }

        [TestMethod]
        public async Task ReconstructWithWrongPassword_ShouldFail()
        {
            var createManager = new CeremonyManager(_testConfig);
            var createInputHandler = new MockInputHandler();
            createManager.InputRequested += createInputHandler.HandleInputRequest;

            createInputHandler.QueueResponse(CreateSecureString("admin123"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(2);
            createInputHandler.QueueResponse(3);
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(CreateSecureString("password123"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(CreateSecureString("password456"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(CreateSecureString("password789"));

            var createResult = await createManager.CreateSharesAsync();
            Assert.IsTrue(createResult.Success);

            var reconstructManager = new CeremonyManager(_testConfig);
            var reconstructInputHandler = new MockInputHandler();
            reconstructManager.InputRequested += reconstructInputHandler.HandleInputRequest;

            reconstructInputHandler.QueueResponse(CreateSecureString("admin123"));
            reconstructInputHandler.QueueResponse(1);
            reconstructInputHandler.QueueResponse(CreateSecureString("wrongpassword")); // Wrong password

            var reconstructResult = await reconstructManager.ReconstructSecretAsync(createResult.OutputFilePath);

            Assert.IsFalse(reconstructResult.Success, "Reconstruction should fail with wrong password");
        }

        [TestMethod]
        public async Task ReconstructWithInsufficientShares_ShouldFail()
        {
            var createManager = new CeremonyManager(_testConfig);
            var createInputHandler = new MockInputHandler();
            createManager.InputRequested += createInputHandler.HandleInputRequest;

            createInputHandler.QueueResponse(CreateSecureString("admin123"));
            createInputHandler.QueueResponse(true);
            createInputHandler.QueueResponse(3); // Threshold = 3
            createInputHandler.QueueResponse(5); // Total = 5
            createInputHandler.QueueResponse(true);
            
            for (int i = 0; i < 5; i++)
            {
                createInputHandler.QueueResponse(false); // Custom keeper
                createInputHandler.QueueResponse($"Keeper {i + 1}");
                createInputHandler.QueueResponse($"555-000{i}");
                createInputHandler.QueueResponse($"keeper{i + 1}@test.com");
                createInputHandler.QueueResponse(CreateSecureString($"password{i}"));
            }

            var createResult = await createManager.CreateSharesAsync();
            Assert.IsTrue(createResult.Success);

            var reconstructManager = new CeremonyManager(_testConfig);
            var reconstructInputHandler = new MockInputHandler();
            reconstructManager.InputRequested += reconstructInputHandler.HandleInputRequest;

            reconstructInputHandler.QueueResponse(CreateSecureString("admin123"));
            reconstructInputHandler.QueueResponse(1); // Keeper 1
            reconstructInputHandler.QueueResponse(CreateSecureString("password0"));
            reconstructInputHandler.QueueResponse(2); // Keeper 2
            reconstructInputHandler.QueueResponse(CreateSecureString("password1"));

            var reconstructResult = await reconstructManager.ReconstructSecretAsync(createResult.OutputFilePath);

            Assert.IsFalse(reconstructResult.Success, "Should fail with insufficient shares");
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
