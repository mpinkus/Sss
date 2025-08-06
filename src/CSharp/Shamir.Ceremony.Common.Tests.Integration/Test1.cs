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
    public sealed class ConsoleIntegrationTests
    {
        private string _testOutputFolder = string.Empty;
        private CeremonyManager? _ceremonyManager;
        private MockInputHandler? _inputHandler;

        [TestInitialize]
        public void Setup()
        {
            _testOutputFolder = Path.Combine(Path.GetTempPath(), $"ShamirTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testOutputFolder);

            var config = new CeremonyConfiguration
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
                    new() { Name = "Test Keeper 2", Phone = "555-0002", Email = "keeper2@test.com", PreferredOrder = 2 }
                }
            };

            _ceremonyManager = new CeremonyManager(config);
            _inputHandler = new MockInputHandler();
            _ceremonyManager.InputRequested += _inputHandler.HandleInputRequest;
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
        public async Task CreateSharesWorkflow_WithValidInputs_ShouldSucceed()
        {
            _inputHandler!.QueueResponse(CreateSecureString("admin123"));
            _inputHandler.QueueResponse(false);
            _inputHandler.QueueResponse("Test Organization");
            _inputHandler.QueueResponse("555-1234");
            _inputHandler.QueueResponse(2);
            _inputHandler.QueueResponse(3);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(false);
            _inputHandler.QueueResponse(false);
            _inputHandler.QueueResponse("Custom Keeper 1");
            _inputHandler.QueueResponse("555-9001");
            _inputHandler.QueueResponse("custom1@test.com");
            _inputHandler.QueueResponse(CreateSecureString("password123"));
            _inputHandler.QueueResponse("Custom Keeper 2");
            _inputHandler.QueueResponse("555-9002");
            _inputHandler.QueueResponse("custom2@test.com");
            _inputHandler.QueueResponse(CreateSecureString("password456"));
            _inputHandler.QueueResponse("Custom Keeper 3");
            _inputHandler.QueueResponse("555-9003");
            _inputHandler.QueueResponse("custom3@test.com");
            _inputHandler.QueueResponse(CreateSecureString("password789"));

            var result = await _ceremonyManager!.CreateSharesAsync();

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");
            Assert.IsNotNull(result.OutputFilePath);
            Assert.IsTrue(File.Exists(result.OutputFilePath));
            Assert.IsNotNull(result.SharesData);
            Assert.AreEqual(3, result.SharesData.Keepers.Count);
            Assert.AreEqual(2, result.SharesData.Configuration.ThresholdRequired);
        }

        [TestMethod]
        public async Task CreateSharesWorkflow_WithDefaultKeepers_ShouldSucceed()
        {
            _inputHandler!.QueueResponse(CreateSecureString("admin123"));
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(2);
            _inputHandler.QueueResponse(2);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(CreateSecureString("password123"));
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(CreateSecureString("password456"));

            var result = await _ceremonyManager!.CreateSharesAsync();

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");
            Assert.IsNotNull(result.OutputFilePath);
            Assert.IsTrue(File.Exists(result.OutputFilePath));
            Assert.IsNotNull(result.SharesData);
            Assert.AreEqual(2, result.SharesData.Keepers.Count);
            Assert.AreEqual("Test Keeper 1", result.SharesData.Keepers[0].Name);
            Assert.AreEqual("Test Keeper 2", result.SharesData.Keepers[1].Name);
        }

        [TestMethod]
        public async Task ReconstructSecretWorkflow_WithValidShares_ShouldSucceed()
        {
            var sharesFilePath = await CreateTestSharesFile();
            
            var reconstructManager = new CeremonyManager(new CeremonyConfiguration
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
                    new() { Name = "Test Keeper 2", Phone = "555-0002", Email = "keeper2@test.com", PreferredOrder = 2 }
                }
            });
            
            var newInputHandler = new MockInputHandler();
            reconstructManager.InputRequested += newInputHandler.HandleInputRequest;

            newInputHandler.QueueResponse(CreateSecureString("admin123"));
            newInputHandler.QueueResponse(1);
            newInputHandler.QueueResponse(CreateSecureString("password123"));
            newInputHandler.QueueResponse(2);
            newInputHandler.QueueResponse(CreateSecureString("password456"));

            var result = await reconstructManager.ReconstructSecretAsync(sharesFilePath);

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");
            Assert.IsNotNull(result.ReconstructedSecret);
            var reconstructedText = Encoding.UTF8.GetString(result.ReconstructedSecret);
            Assert.AreEqual("Test Secret 123", reconstructedText);
        }

        [TestMethod]
        public async Task ReconstructSecretWorkflow_WithInvalidFile_ShouldFail()
        {
            var invalidFilePath = Path.Combine(_testOutputFolder, "nonexistent.json");

            _inputHandler!.QueueResponse(CreateSecureString("admin123"));
            _inputHandler.QueueResponse(invalidFilePath);

            var result = await _ceremonyManager!.ReconstructSecretAsync();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("not found") || result.Message.Contains("does not exist") || result.Message.Contains("Failed"));
        }

        [TestMethod]
        public async Task ReconstructSecretWorkflow_WithWrongPassword_ShouldFail()
        {
            var sharesFilePath = await CreateTestSharesFile();
            var wrongPassword = CreateSecureString("wrongpassword");

            _inputHandler!.QueueResponse(sharesFilePath);
            _inputHandler.QueueResponse(1);
            _inputHandler.QueueResponse(wrongPassword);

            try
            {
                var result = await _ceremonyManager!.ReconstructSecretAsync();
                Assert.IsFalse(result.Success);
            }
            catch (Exception ex)
            {
                Assert.IsTrue(ex.Message.Contains("decrypt") || ex.Message.Contains("HMAC") || ex.Message.Contains("failed"));
            }
        }

        [TestMethod]
        public async Task CreateSharesWorkflow_WithValidationEvents_ShouldSucceed()
        {
            var validOrgName = "Test Organization";
            var validPhone = "555-1234";
            var validShares = 2;
            var validThreshold = 2;
            var validSecret = CreateSecureString("Test Secret");
            var validPassword = CreateSecureString("password123");

            _inputHandler!.QueueResponse(CreateSecureString("admin123"));
            _inputHandler.QueueResponse(false);
            _inputHandler.QueueResponse(validOrgName);
            _inputHandler.QueueResponse(validPhone);
            _inputHandler.QueueResponse(validThreshold);
            _inputHandler.QueueResponse(validShares);
            _inputHandler.QueueResponse(false);
            _inputHandler.QueueResponse(validSecret);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(validPassword);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(validPassword);

            var validationEvents = new List<ValidationEventArgs>();
            _ceremonyManager!.ValidationResult += (sender, e) => validationEvents.Add(e);

            var result = await _ceremonyManager.CreateSharesAsync();

            Assert.IsTrue(result.Success);
        }

        [TestMethod]
        public async Task FinalizeSession_ShouldCreateSessionFiles()
        {
            _inputHandler!.QueueResponse(CreateSecureString("admin123"));
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(2);
            _inputHandler.QueueResponse(2);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(CreateSecureString("password123"));
            _inputHandler.QueueResponse(true);
            _inputHandler.QueueResponse(CreateSecureString("password456"));

            await _ceremonyManager!.CreateSharesAsync();
            _ceremonyManager.FinalizeSession();

            var allFiles = Directory.GetFiles(_testOutputFolder, "*", SearchOption.AllDirectories);
            Assert.IsTrue(allFiles.Length > 0, $"Expected session files to be created. Found files: {string.Join(", ", allFiles.Select(Path.GetFileName))}");
        }

        private async Task<string> CreateTestSharesFile()
        {
            var testCeremonyManager = new CeremonyManager(new CeremonyConfiguration
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
                    new() { Name = "Test Keeper 2", Phone = "555-0002", Email = "keeper2@test.com", PreferredOrder = 2 }
                }
            });
            
            var testInputHandler = new MockInputHandler();
            testCeremonyManager.InputRequested += testInputHandler.HandleInputRequest;

            testInputHandler.QueueResponse(CreateSecureString("admin123"));
            testInputHandler.QueueResponse(false);
            testInputHandler.QueueResponse("Test Organization");
            testInputHandler.QueueResponse("555-1234");
            testInputHandler.QueueResponse(2);
            testInputHandler.QueueResponse(3);
            testInputHandler.QueueResponse(false);
            testInputHandler.QueueResponse(CreateSecureString("Test Secret 123"));
            testInputHandler.QueueResponse(false);
            testInputHandler.QueueResponse(false);
            testInputHandler.QueueResponse("Test Keeper 1");
            testInputHandler.QueueResponse("555-9001");
            testInputHandler.QueueResponse("keeper1@test.com");
            testInputHandler.QueueResponse(CreateSecureString("password123"));
            testInputHandler.QueueResponse("Test Keeper 2");
            testInputHandler.QueueResponse("555-9002");
            testInputHandler.QueueResponse("keeper2@test.com");
            testInputHandler.QueueResponse(CreateSecureString("password456"));
            testInputHandler.QueueResponse("Test Keeper 3");
            testInputHandler.QueueResponse("555-9003");
            testInputHandler.QueueResponse("keeper3@test.com");
            testInputHandler.QueueResponse(CreateSecureString("password789"));

            var result = await testCeremonyManager.CreateSharesAsync();
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
                    Console.WriteLine($"DEBUG: No response queued for request: {e.RequestType}, Prompt: {e.Prompt}");
                    e.CompletionSource.SetException(new InvalidOperationException($"No response queued for request: {e.Prompt}"));
                }
            }
        }
    }
}
