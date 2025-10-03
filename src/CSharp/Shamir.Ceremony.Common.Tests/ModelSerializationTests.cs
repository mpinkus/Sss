using System.Text.Json;
using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class ModelSerializationTests
    {
        [TestMethod]
        public void Share_SerializationRoundTrip_ShouldPreserveData()
        {
            var share = new Share { X = 5, Y = "ABCDEF123456" };

            var json = JsonSerializer.Serialize(share);
            var deserialized = JsonSerializer.Deserialize<Share>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(share.X, deserialized.X);
            Assert.AreEqual(share.Y, deserialized.Y);
        }

        [TestMethod]
        public void SecretKeeperRecord_SerializationRoundTrip_ShouldPreserveData()
        {
            var keeper = new SecretKeeperRecord
            {
                Id = "keeper-1",
                ShareNumber = 1,
                Name = "John Doe",
                Phone = "555-1234",
                Email = "john@example.com",
                EncryptedShare = "encrypteddata",
                Salt = "salt123",
                IV = "iv123",
                Hmac = "hmac123",
                SessionId = "session-123",
                CreatedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(keeper);
            var deserialized = JsonSerializer.Deserialize<SecretKeeperRecord>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(keeper.Id, deserialized.Id);
            Assert.AreEqual(keeper.ShareNumber, deserialized.ShareNumber);
            Assert.AreEqual(keeper.Name, deserialized.Name);
            Assert.AreEqual(keeper.Phone, deserialized.Phone);
            Assert.AreEqual(keeper.Email, deserialized.Email);
            Assert.AreEqual(keeper.EncryptedShare, deserialized.EncryptedShare);
            Assert.AreEqual(keeper.Salt, deserialized.Salt);
            Assert.AreEqual(keeper.IV, deserialized.IV);
            Assert.AreEqual(keeper.Hmac, deserialized.Hmac);
            Assert.AreEqual(keeper.SessionId, deserialized.SessionId);
        }

        [TestMethod]
        public void ShamirSecretOutput_SerializationRoundTrip_ShouldPreserveData()
        {
            var output = new ShamirSecretOutput
            {
                Version = "1.0.0",
                SessionId = "session123",
                Organization = new OrganizationInfo
                {
                    Name = "Test Org",
                    ContactPhone = "555-1234"
                },
                Configuration = new ShamirConfiguration
                {
                    TotalShares = 5,
                    ThresholdRequired = 3,
                    Algorithm = "Shamir-GF256",
                    EncryptionAlgorithm = "AES-256-GCM",
                    KdfAlgorithm = "PBKDF2-SHA256",
                    KdfIterations = 100000
                },
                MasterSecretHash = "hash123",
                CreatedAt = DateTime.UtcNow,
                Keepers = new List<SecretKeeperRecord>
                {
                    new() { Name = "Keeper 1", Email = "k1@test.com" },
                    new() { Name = "Keeper 2", Email = "k2@test.com" }
                }
            };

            var json = JsonSerializer.Serialize(output);
            var deserialized = JsonSerializer.Deserialize<ShamirSecretOutput>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(output.Version, deserialized.Version);
            Assert.AreEqual(output.SessionId, deserialized.SessionId);
            Assert.AreEqual(output.Organization.Name, deserialized.Organization.Name);
            Assert.AreEqual(output.Configuration.TotalShares, deserialized.Configuration.TotalShares);
            Assert.AreEqual(output.Configuration.ThresholdRequired, deserialized.Configuration.ThresholdRequired);
            Assert.AreEqual(output.MasterSecretHash, deserialized.MasterSecretHash);
            Assert.AreEqual(output.Keepers.Count, deserialized.Keepers.Count);
        }

        [TestMethod]
        public void SessionInfo_SerializationRoundTrip_ShouldPreserveData()
        {
            var sessionInfo = new SessionInfo
            {
                SessionId = "session123",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1),
                Duration = TimeSpan.FromHours(1),
                MachineName = "TEST-MACHINE",
                UserName = "testuser",
                ApplicationVersion = "1.0.0",
                OutputFolder = "/tmp/test",
                Organization = new OrganizationInfo { Name = "Test Org", ContactPhone = "555-1234" },
                Events = new List<SessionEvent>
                {
                    new() { Timestamp = DateTime.UtcNow, EventType = "TEST", Description = "Test event" }
                },
                SharesCreated = new List<ShareCreationRecord>
                {
                    new() { Timestamp = DateTime.UtcNow, TotalShares = 5, ThresholdRequired = 3 }
                },
                SharesRecovered = new List<ShareRecoveryRecord>
                {
                    new() { Timestamp = DateTime.UtcNow, Success = true }
                },
                Summary = new SessionSummary
                {
                    TotalSharesCreated = 5,
                    TotalShareSets = 1,
                    TotalRecoveryAttempts = 1,
                    SuccessfulRecoveries = 1,
                    FailedRecoveries = 0,
                    TotalEvents = 1
                }
            };

            var json = JsonSerializer.Serialize(sessionInfo);
            var deserialized = JsonSerializer.Deserialize<SessionInfo>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(sessionInfo.SessionId, deserialized.SessionId);
            Assert.AreEqual(sessionInfo.MachineName, deserialized.MachineName);
            Assert.AreEqual(sessionInfo.UserName, deserialized.UserName);
            Assert.AreEqual(sessionInfo.Events.Count, deserialized.Events.Count);
            Assert.AreEqual(sessionInfo.SharesCreated.Count, deserialized.SharesCreated.Count);
            Assert.AreEqual(sessionInfo.SharesRecovered.Count, deserialized.SharesRecovered.Count);
            Assert.IsNotNull(deserialized.Summary);
        }

        [TestMethod]
        public void AuditLogEntry_SerializationRoundTrip_ShouldPreserveData()
        {
            var entry = new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow,
                SessionId = "session123",
                EventType = "CREATE_SHARES",
                Message = "Shares created successfully",
                User = "testuser",
                Machine = "TEST-MACHINE"
            };

            var json = JsonSerializer.Serialize(entry);
            var deserialized = JsonSerializer.Deserialize<AuditLogEntry>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(entry.SessionId, deserialized.SessionId);
            Assert.AreEqual(entry.EventType, deserialized.EventType);
            Assert.AreEqual(entry.Message, deserialized.Message);
            Assert.AreEqual(entry.User, deserialized.User);
            Assert.AreEqual(entry.Machine, deserialized.Machine);
        }

        [TestMethod]
        public void ShamirSecretOutput_WithNullFields_ShouldSerializeCorrectly()
        {
            var output = new ShamirSecretOutput
            {
                Version = "1.0.0",
                SessionId = "session123",
                Organization = null, // Null field
                Configuration = new ShamirConfiguration
                {
                    TotalShares = 5,
                    ThresholdRequired = 3
                },
                Keepers = new List<SecretKeeperRecord>()
            };

            var json = JsonSerializer.Serialize(output);
            var deserialized = JsonSerializer.Deserialize<ShamirSecretOutput>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(output.Version, deserialized.Version);
        }

        [TestMethod]
        public void Share_WithSpecialCharacters_ShouldSerializeCorrectly()
        {
            var share = new Share { X = 1, Y = "AB/CD+EF==" };

            var json = JsonSerializer.Serialize(share);
            var deserialized = JsonSerializer.Deserialize<Share>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(share.Y, deserialized.Y);
        }

        [TestMethod]
        public void SessionOutput_CompleteObject_ShouldSerializeCorrectly()
        {
            var sessionOutput = new SessionOutput
            {
                SessionData = new SessionInfo
                {
                    SessionId = "session123",
                    StartTime = DateTime.UtcNow,
                    MachineName = "TEST",
                    UserName = "user",
                    ApplicationVersion = "1.0.0",
                    Events = new List<SessionEvent>()
                },
                SessionDataHash = "hash123",
                AdminSessionHmac = "hmac123",
                HmacAlgorithm = "HMAC-SHA256",
                SignatureTimestamp = DateTime.UtcNow,
                SignatureNote = "Test signature"
            };

            var json = JsonSerializer.Serialize(sessionOutput);
            var deserialized = JsonSerializer.Deserialize<SessionOutput>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(sessionOutput.SessionDataHash, deserialized.SessionDataHash);
            Assert.AreEqual(sessionOutput.AdminSessionHmac, deserialized.AdminSessionHmac);
            Assert.AreEqual(sessionOutput.HmacAlgorithm, deserialized.HmacAlgorithm);
        }
    }
}
