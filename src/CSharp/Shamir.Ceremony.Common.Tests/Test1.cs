using System.Security;
using System.Text;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Cryptography;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Tests;

[TestClass]
public sealed class CeremonyManagerTests
{
    private CeremonyConfiguration GetTestConfiguration()
    {
        return new CeremonyConfiguration
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
                OutputFolder = Path.Combine(Path.GetTempPath(), "ShamirTest")
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

    private SecureString CreateSecureString(string value)
    {
        var secureString = new SecureString();
        foreach (char c in value)
        {
            secureString.AppendChar(c);
        }
        secureString.MakeReadOnly();
        return secureString;
    }

    [TestMethod]
    public void ShamirSecretShare_GenerateAndReconstructShares_ShouldWork()
    {
        var secret = Encoding.UTF8.GetBytes("This is a test secret");
        int threshold = 3;
        int totalShares = 5;

        var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

        Assert.AreEqual(totalShares, shares.Count);

        var reconstructedSecret = ShamirSecretShare.ReconstructSecret(shares.Take(threshold).ToList(), threshold);

        CollectionAssert.AreEqual(secret, reconstructedSecret);
    }

    [TestMethod]
    public void ShamirSecretShare_InsufficientShares_ShouldThrow()
    {
        var secret = Encoding.UTF8.GetBytes("Test secret");
        int threshold = 3;
        int totalShares = 5;

        var shares = ShamirSecretShare.GenerateShares(secret, threshold, totalShares);

        Assert.ThrowsException<ArgumentException>(() =>
        {
            ShamirSecretShare.ReconstructSecret(shares.Take(threshold - 1).ToList(), threshold);
        });
    }

    [TestMethod]
    public void CryptographyService_EncryptDecryptShare_ShouldWork()
    {
        var config = GetTestConfiguration();
        var cryptoService = new CryptographyService(config.Security);
        var share = new Share { X = 1, Y = "dGVzdCBzaGFyZQ==" };
        var password = CreateSecureString("testpassword123");

        var (encryptedData, hmac) = cryptoService.EncryptShare(share, password, out string salt, out string iv);

        var keeper = new SecretKeeperRecord
        {
            EncryptedShare = encryptedData,
            Hmac = hmac,
            Salt = salt,
            IV = iv
        };

        var decryptedShare = cryptoService.DecryptShare(keeper, password, config.Security.KdfIterations);

        Assert.AreEqual(share.X, decryptedShare.X);
        Assert.AreEqual(share.Y, decryptedShare.Y);
    }

    [TestMethod]
    public void CryptographyService_WrongPassword_ShouldThrow()
    {
        var config = GetTestConfiguration();
        var cryptoService = new CryptographyService(config.Security);
        var share = new Share { X = 1, Y = "dGVzdCBzaGFyZQ==" };
        var correctPassword = CreateSecureString("testpassword123");
        var wrongPassword = CreateSecureString("wrongpassword");

        var (encryptedData, hmac) = cryptoService.EncryptShare(share, correctPassword, out string salt, out string iv);

        var keeper = new SecretKeeperRecord
        {
            EncryptedShare = encryptedData,
            Hmac = hmac,
            Salt = salt,
            IV = iv
        };

        Assert.ThrowsException<System.Security.Cryptography.CryptographicException>(() =>
        {
            cryptoService.DecryptShare(keeper, wrongPassword, config.Security.KdfIterations);
        });
    }

    [TestMethod]
    public void AuditLogger_LogAudit_ShouldCreateEntries()
    {
        var config = GetTestConfiguration();
        var sessionId = "test-session";
        var outputFolder = Path.Combine(Path.GetTempPath(), "AuditTest");
        Directory.CreateDirectory(outputFolder);

        var auditLogger = new AuditLogger(config.Security, sessionId, outputFolder);

        auditLogger.LogAudit("TEST_EVENT", "Test message");
        auditLogger.LogAudit("ANOTHER_EVENT", "Another test message");

        var entries = auditLogger.GetAuditEntries();

        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual("TEST_EVENT", entries[0].EventType);
        Assert.AreEqual("Test message", entries[0].Message);
        Assert.AreEqual(sessionId, entries[0].SessionId);

        Directory.Delete(outputFolder, true);
    }

    [TestMethod]
    public void CeremonyConfiguration_FromConfiguration_ShouldUseDefaults()
    {
        var emptyConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var ceremonyConfig = CeremonyConfiguration.FromConfiguration(emptyConfig);

        Assert.IsTrue(ceremonyConfig.Security.ConfirmationRequired);
        Assert.AreEqual(12, ceremonyConfig.Security.MinPasswordLength);
        Assert.AreEqual(100000, ceremonyConfig.Security.KdfIterations);
        Assert.IsTrue(ceremonyConfig.Security.AuditLogEnabled);
    }

    [TestMethod]
    public void SessionManager_AddEvents_ShouldTrackEvents()
    {
        var config = GetTestConfiguration();
        var cryptoService = new CryptographyService(config.Security);
        var sessionId = "test-session";
        var outputFolder = Path.Combine(Path.GetTempPath(), "SessionTest");
        Directory.CreateDirectory(outputFolder);
        var adminKey = new byte[32];

        var sessionManager = new SessionManager(sessionId, outputFolder, adminKey, cryptoService);

        sessionManager.AddEvent("TEST_EVENT", "Test event description");
        sessionManager.AddEvent("ANOTHER_EVENT", "Another event description");

        Assert.AreEqual(2, sessionManager.SessionInfo.Events.Count);
        Assert.AreEqual("TEST_EVENT", sessionManager.SessionInfo.Events[0].EventType);
        Assert.AreEqual("Test event description", sessionManager.SessionInfo.Events[0].Description);

        Directory.Delete(outputFolder, true);
    }

    [TestMethod]
    public void CeremonyManager_Configuration_ShouldInitialize()
    {
        var config = GetTestConfiguration();
        var manager = new CeremonyManager(config);

        Assert.IsNotNull(manager);
    }

    [TestMethod]
    public void Share_SerializationDeserialization_ShouldWork()
    {
        var originalShare = new Share { X = 5, Y = "dGVzdCBzaGFyZSBkYXRh" };
        
        var json = System.Text.Json.JsonSerializer.Serialize(originalShare);
        var deserializedShare = System.Text.Json.JsonSerializer.Deserialize<Share>(json);

        Assert.IsNotNull(deserializedShare);
        Assert.AreEqual(originalShare.X, deserializedShare.X);
        Assert.AreEqual(originalShare.Y, deserializedShare.Y);
    }

    [TestMethod]
    public void ShamirSecretOutput_CompleteWorkflow_ShouldSerialize()
    {
        var output = new ShamirSecretOutput
        {
            Version = "1.0.0",
            SessionId = "test-session",
            Organization = new OrganizationInfo { Name = "Test Org", ContactPhone = "555-1234" },
            Configuration = new ShamirConfiguration
            {
                TotalShares = 5,
                ThresholdRequired = 3,
                Algorithm = "Shamir-GF256",
                EncryptionAlgorithm = "AES-256-GCM",
                KdfAlgorithm = "PBKDF2-SHA256",
                KdfIterations = 100000
            },
            MasterSecretHash = "test-hash",
            CreatedAt = DateTime.UtcNow,
            Keepers = new List<SecretKeeperRecord>
            {
                new() { Name = "Test Keeper", Email = "test@example.com", ShareNumber = 1 }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ShamirSecretOutput>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(output.SessionId, deserialized.SessionId);
        Assert.AreEqual(output.Configuration.TotalShares, deserialized.Configuration.TotalShares);
        Assert.AreEqual(output.Keepers.Count, deserialized.Keepers.Count);
    }
}
