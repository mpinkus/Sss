using System.Security;
using System.Text;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class CeremonyManagerComprehensiveTests
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
                    new() { Name = "Test Keeper 1", Email = "keeper1@test.com", Phone = "555-0001", PreferredOrder = 1 },
                    new() { Name = "Test Keeper 2", Email = "keeper2@test.com", Phone = "555-0002", PreferredOrder = 2 }
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
        public void CeremonyManager_Constructor_WithNullConfig_ShouldThrow()
        {
            Assert.ThrowsException<NullReferenceException>(() => new CeremonyManager(null!));
        }

        [TestMethod]
        public async Task CreateSharesAsync_WithValidInputs_ShouldSucceed()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            inputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            inputHandler.QueueResponse(false); // Don't use default organization
            inputHandler.QueueResponse("Test Organization"); // Organization name
            inputHandler.QueueResponse("555-1234"); // Organization phone
            inputHandler.QueueResponse(2); // Threshold (asked first)
            inputHandler.QueueResponse(3); // Total shares (asked second)
            inputHandler.QueueResponse(true); // Generate random secret
            inputHandler.QueueResponse(true); // Use default keeper 1
            inputHandler.QueueResponse(CreateSecureString("password123")); // Password for keeper 1
            inputHandler.QueueResponse(true); // Use default keeper 2
            inputHandler.QueueResponse(CreateSecureString("password456")); // Password for keeper 2
            inputHandler.QueueResponse("Custom Keeper"); // Name for keeper 3
            inputHandler.QueueResponse("555-9999"); // Phone for keeper 3
            inputHandler.QueueResponse("custom@test.com"); // Email for keeper 3
            inputHandler.QueueResponse(CreateSecureString("password789")); // Password for keeper 3

            var result = await manager.CreateSharesAsync();

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");
            Assert.IsNotNull(result.OutputFilePath);
            Assert.IsTrue(File.Exists(result.OutputFilePath));
            Assert.IsNotNull(result.SharesData);
            Assert.AreEqual(3, result.SharesData.Keepers.Count);
            Assert.AreEqual(2, result.SharesData.Configuration.ThresholdRequired);
        }

        [TestMethod]
        public async Task CreateSharesAsync_WithCustomKeepers_ShouldSucceed()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            inputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            inputHandler.QueueResponse(false); // Don't use default organization
            inputHandler.QueueResponse("Test Organization"); // Organization name
            inputHandler.QueueResponse("555-1234"); // Organization phone
            inputHandler.QueueResponse(2); // Threshold (asked first)
            inputHandler.QueueResponse(2); // Total shares (asked second)
            inputHandler.QueueResponse(true); // Generate random secret
            inputHandler.QueueResponse(false); // Don't use default keeper 1
            inputHandler.QueueResponse(false); // Don't use default keeper 2
            inputHandler.QueueResponse("Custom Keeper 1"); // Name for keeper 1
            inputHandler.QueueResponse("555-9001"); // Phone for keeper 1
            inputHandler.QueueResponse("custom1@test.com"); // Email for keeper 1
            inputHandler.QueueResponse(CreateSecureString("password123")); // Password for keeper 1
            inputHandler.QueueResponse("Custom Keeper 2"); // Name for keeper 2
            inputHandler.QueueResponse("555-9002"); // Phone for keeper 2
            inputHandler.QueueResponse("custom2@test.com"); // Email for keeper 2
            inputHandler.QueueResponse(CreateSecureString("password456")); // Password for keeper 2

            var result = await manager.CreateSharesAsync();

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");
            Assert.IsNotNull(result.SharesData);
            Assert.AreEqual("Custom Keeper 1", result.SharesData.Keepers[0].Name);
            Assert.AreEqual("Custom Keeper 2", result.SharesData.Keepers[1].Name);
        }

        [TestMethod]
        public async Task ReconstructSecretAsync_WithValidShares_ShouldSucceed()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var sharesFilePath = await CreateTestSharesFile(manager, inputHandler);

            var reconstructManager = new CeremonyManager(_testConfig);
            var reconstructInputHandler = new MockInputHandler();
            reconstructManager.InputRequested += reconstructInputHandler.HandleInputRequest;

            reconstructInputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            reconstructInputHandler.QueueResponse(1); // Select keeper 1
            reconstructInputHandler.QueueResponse(CreateSecureString("password123")); // Password for keeper 1
            reconstructInputHandler.QueueResponse(2); // Select keeper 2
            reconstructInputHandler.QueueResponse(CreateSecureString("password456")); // Password for keeper 2

            var result = await reconstructManager.ReconstructSecretAsync(sharesFilePath);

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");
            Assert.IsNotNull(result.ReconstructedSecret);
        }

        [TestMethod]
        public async Task ReconstructSecretAsync_WithInvalidFilePath_ShouldFail()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var invalidPath = Path.Combine(_testOutputFolder, "nonexistent.json");
            inputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            inputHandler.QueueResponse(invalidPath); // Invalid file path

            var result = await manager.ReconstructSecretAsync();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("not found") || result.Message.Contains("does not exist") || result.Message.Contains("Failed"));
        }

        [TestMethod]
        public async Task CreateSharesAsync_ShouldFireProgressEvents()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var progressEvents = new List<ProgressEventArgs>();
            manager.ProgressUpdated += (sender, e) => progressEvents.Add(e);

            inputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            inputHandler.QueueResponse(true); // Use default organization
            inputHandler.QueueResponse(2); // Threshold (asked first)
            inputHandler.QueueResponse(2); // Total shares (asked second)
            inputHandler.QueueResponse(true); // Generate random secret
            inputHandler.QueueResponse(true); // Use default keeper 1
            inputHandler.QueueResponse(CreateSecureString("password123")); // Password for keeper 1
            inputHandler.QueueResponse(true); // Use default keeper 2
            inputHandler.QueueResponse(CreateSecureString("password456")); // Password for keeper 2

            await manager.CreateSharesAsync();

            Assert.IsTrue(progressEvents.Count > 0);
            Assert.IsTrue(progressEvents.Any(e => e.EventType == "SESSION_INIT"));
            Assert.IsTrue(progressEvents.Any(e => e.EventType == "CREATE_START"));
        }

        [TestMethod]
        public async Task CreateSharesAsync_ShouldFireCompletionEvent()
        {
            var manager = new CeremonyManager(_testConfig);
            var inputHandler = new MockInputHandler();
            manager.InputRequested += inputHandler.HandleInputRequest;

            var completionEvents = new List<CompletionEventArgs>();
            manager.OperationCompleted += (sender, e) => completionEvents.Add(e);

            inputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            inputHandler.QueueResponse(true); // Use default organization
            inputHandler.QueueResponse(2); // Threshold (asked first)
            inputHandler.QueueResponse(2); // Total shares (asked second)
            inputHandler.QueueResponse(true); // Generate random secret
            inputHandler.QueueResponse(true); // Use default keeper 1
            inputHandler.QueueResponse(CreateSecureString("password123")); // Password for keeper 1
            inputHandler.QueueResponse(true); // Use default keeper 2
            inputHandler.QueueResponse(CreateSecureString("password456")); // Password for keeper 2

            await manager.CreateSharesAsync();

            Assert.IsTrue(completionEvents.Count > 0);
            Assert.IsTrue(completionEvents.Any(e => e.OperationType == "CREATE_SHARES" && e.Success));
        }

        private async Task<string> CreateTestSharesFile(CeremonyManager manager, MockInputHandler inputHandler)
        {
            inputHandler.QueueResponse(CreateSecureString("admin123")); // Admin session password
            inputHandler.QueueResponse(false); // Don't use default organization
            inputHandler.QueueResponse("Test Organization"); // Organization name
            inputHandler.QueueResponse("555-1234"); // Organization phone
            inputHandler.QueueResponse(2); // Threshold (asked first)
            inputHandler.QueueResponse(3); // Total shares (asked second)
            inputHandler.QueueResponse(false); // Don't generate random secret
            inputHandler.QueueResponse(CreateSecureString("Test Secret 123")); // User provided secret
            inputHandler.QueueResponse(false); // Don't use default keeper 1
            inputHandler.QueueResponse(false); // Don't use default keeper 2
            inputHandler.QueueResponse("Test Keeper 1"); // Name for keeper 1
            inputHandler.QueueResponse("555-9001"); // Phone for keeper 1
            inputHandler.QueueResponse("keeper1@test.com"); // Email for keeper 1
            inputHandler.QueueResponse(CreateSecureString("password123")); // Password for keeper 1
            inputHandler.QueueResponse("Test Keeper 2"); // Name for keeper 2
            inputHandler.QueueResponse("555-9002"); // Phone for keeper 2
            inputHandler.QueueResponse("keeper2@test.com"); // Email for keeper 2
            inputHandler.QueueResponse(CreateSecureString("password456")); // Password for keeper 2
            inputHandler.QueueResponse("Test Keeper 3"); // Name for keeper 3
            inputHandler.QueueResponse("555-9003"); // Phone for keeper 3
            inputHandler.QueueResponse("keeper3@test.com"); // Email for keeper 3
            inputHandler.QueueResponse(CreateSecureString("password789")); // Password for keeper 3

            var result = await manager.CreateSharesAsync();
            return result.OutputFilePath!;
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
                    Console.WriteLine($"DEBUG: Request Type: {e.RequestType}, Prompt: {e.Prompt}, Response Type: {response.GetType().Name}, Response: {response}");
                    e.CompletionSource.SetResult(response);
                }
                else
                {
                    Console.WriteLine($"DEBUG: No response queued for request: {e.RequestType} - {e.Prompt}");
                    e.CompletionSource.SetException(new InvalidOperationException($"No response queued for request: {e.Prompt}"));
                }
            }
        }
    }
}
