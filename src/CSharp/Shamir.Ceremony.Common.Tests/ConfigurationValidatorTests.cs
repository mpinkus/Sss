using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Configuration.Validators;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class ConfigurationValidatorTests
    {
        [TestMethod]
        public void SecuritySettingsValidator_ValidSettings_ShouldPass()
        {
            var settings = new SecuritySettings
            {
                MinPasswordLength = 12,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireSpecialCharacter = true,
                KdfIterations = 100000,
                SecureDeletePasses = 3,
                AuditLogEnabled = true,
                AuditLogRetentionDays = 90
            };

            var validator = new SecuritySettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void SecuritySettingsValidator_InvalidMinPasswordLength_ShouldFail()
        {
            var settings = new SecuritySettings
            {
                MinPasswordLength = 0,
                KdfIterations = 100000,
                SecureDeletePasses = 3
            };

            var validator = new SecuritySettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(SecuritySettings.MinPasswordLength)));
        }

        [TestMethod]
        public void SecuritySettingsValidator_InvalidKdfIterations_ShouldFail()
        {
            var settings = new SecuritySettings
            {
                MinPasswordLength = 12,
                KdfIterations = 100, // Too low
                SecureDeletePasses = 3
            };

            var validator = new SecuritySettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(SecuritySettings.KdfIterations)));
        }

        [TestMethod]
        public void SecuritySettingsValidator_InvalidSecureDeletePasses_ShouldFail()
        {
            var settings = new SecuritySettings
            {
                MinPasswordLength = 12,
                KdfIterations = 100000,
                SecureDeletePasses = 0
            };

            var validator = new SecuritySettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(SecuritySettings.SecureDeletePasses)));
        }

        [TestMethod]
        public void SecuritySettingsValidator_InvalidAuditLogRetentionDays_ShouldFail()
        {
            var settings = new SecuritySettings
            {
                MinPasswordLength = 12,
                KdfIterations = 100000,
                SecureDeletePasses = 3,
                AuditLogRetentionDays = -1
            };

            var validator = new SecuritySettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(SecuritySettings.AuditLogRetentionDays)));
        }

        [TestMethod]
        public void FileSystemSettingsValidator_ValidPath_ShouldPass()
        {
            var settings = new FileSystemSettings
            {
                OutputFolder = Path.GetTempPath()
            };

            var validator = new FileSystemSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void FileSystemSettingsValidator_EmptyPath_ShouldFail()
        {
            var settings = new FileSystemSettings
            {
                OutputFolder = string.Empty
            };

            var validator = new FileSystemSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(FileSystemSettings.OutputFolder)));
        }

        [TestMethod]
        public void OrganizationSettingsValidator_ValidSettings_ShouldPass()
        {
            var settings = new OrganizationSettings
            {
                Name = "Test Organization",
                ContactPhone = "555-1234"
            };

            var validator = new OrganizationSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void OrganizationSettingsValidator_EmptyName_ShouldFail()
        {
            var settings = new OrganizationSettings
            {
                Name = string.Empty,
                ContactPhone = "555-1234"
            };

            var validator = new OrganizationSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(OrganizationSettings.Name)));
        }

        [TestMethod]
        public void DefaultKeeperSettingsValidator_ValidSettings_ShouldPass()
        {
            var settings = new DefaultKeeperSettings
            {
                Name = "John Doe",
                Phone = "555-1234",
                Email = "john.doe@example.com",
                PreferredOrder = 1
            };

            var validator = new DefaultKeeperSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void DefaultKeeperSettingsValidator_InvalidEmail_ShouldFail()
        {
            var settings = new DefaultKeeperSettings
            {
                Name = "John Doe",
                Phone = "555-1234",
                Email = "invalid-email",
                PreferredOrder = 1
            };

            var validator = new DefaultKeeperSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(DefaultKeeperSettings.Email)));
        }

        [TestMethod]
        public void DefaultKeeperSettingsValidator_EmptyName_ShouldFail()
        {
            var settings = new DefaultKeeperSettings
            {
                Name = string.Empty,
                Phone = "555-1234",
                Email = "john.doe@example.com",
                PreferredOrder = 1
            };

            var validator = new DefaultKeeperSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(DefaultKeeperSettings.Name)));
        }

        [TestMethod]
        public void MongoDbSettingsValidator_ValidSettings_ShouldPass()
        {
            var settings = new MongoDbSettings
            {
                ConnectionString = "mongodb://localhost:27017",
                DatabaseName = "TestDB",
                CollectionName = "TestCollection"
            };

            var validator = new MongoDbSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void MongoDbSettingsValidator_EmptyConnectionString_ShouldFail()
        {
            var settings = new MongoDbSettings
            {
                ConnectionString = string.Empty,
                DatabaseName = "TestDB",
                CollectionName = "TestCollection"
            };

            var validator = new MongoDbSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(MongoDbSettings.ConnectionString)));
        }

        [TestMethod]
        public void MongoDbSettingsValidator_EmptyDatabaseName_ShouldFail()
        {
            var settings = new MongoDbSettings
            {
                ConnectionString = "mongodb://localhost:27017",
                DatabaseName = string.Empty,
                CollectionName = "TestCollection"
            };

            var validator = new MongoDbSettingsValidator();
            var result = validator.Validate(settings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(MongoDbSettings.DatabaseName)));
        }

        [TestMethod]
        public void CeremonyConfigurationValidator_ValidConfiguration_ShouldPass()
        {
            var config = new CeremonyConfiguration
            {
                Security = new SecuritySettings
                {
                    MinPasswordLength = 12,
                    KdfIterations = 100000,
                    SecureDeletePasses = 3,
                    AuditLogRetentionDays = 90
                },
                FileSystem = new FileSystemSettings
                {
                    OutputFolder = Path.GetTempPath()
                },
                Organization = new OrganizationSettings
                {
                    Name = "Test Org",
                    ContactPhone = "555-1234"
                },
                DefaultKeepers = new List<DefaultKeeperSettings>
                {
                    new() { Name = "Keeper 1", Email = "k1@test.com", Phone = "555-0001", PreferredOrder = 1 }
                },
                MongoDb = new MongoDbSettings
                {
                    ConnectionString = "mongodb://localhost:27017",
                    DatabaseName = "TestDB",
                    CollectionName = "TestCollection"
                }
            };

            var validator = new CeremonyConfigurationValidator();
            var result = validator.Validate(config);

            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void CeremonyConfigurationValidator_InvalidNestedSettings_ShouldFail()
        {
            var config = new CeremonyConfiguration
            {
                Security = new SecuritySettings
                {
                    MinPasswordLength = 0, // Invalid
                    KdfIterations = 100,   // Invalid
                    SecureDeletePasses = 3
                },
                FileSystem = new FileSystemSettings
                {
                    OutputFolder = string.Empty // Invalid
                },
                Organization = new OrganizationSettings
                {
                    Name = string.Empty, // Invalid
                    ContactPhone = "555-1234"
                }
            };

            var validator = new CeremonyConfigurationValidator();
            var result = validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Count > 0);
        }
    }
}
