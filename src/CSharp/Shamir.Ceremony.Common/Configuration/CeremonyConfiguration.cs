using Microsoft.Extensions.Configuration;

namespace Shamir.Ceremony.Common.Configuration;

public class CeremonyConfiguration
{
    public SecuritySettings Security { get; set; } = new();
    public FileSystemSettings FileSystem { get; set; } = new();
    public OrganizationSettings Organization { get; set; } = new();
    public List<DefaultKeeperSettings> DefaultKeepers { get; set; } = new();

    public static CeremonyConfiguration FromConfiguration(IConfiguration configuration)
    {
        var config = new CeremonyConfiguration();
        
        config.Security.ConfirmationRequired = configuration.GetValue<bool>("SecuritySettings:ConfirmationRequired", true);
        config.Security.MinPasswordLength = configuration.GetValue<int>("SecuritySettings:MinPasswordLength", 12);
        config.Security.RequireUppercase = configuration.GetValue<bool>("SecuritySettings:RequireUppercase", true);
        config.Security.RequireLowercase = configuration.GetValue<bool>("SecuritySettings:RequireLowercase", true);
        config.Security.RequireDigit = configuration.GetValue<bool>("SecuritySettings:RequireDigit", true);
        config.Security.RequireSpecialCharacter = configuration.GetValue<bool>("SecuritySettings:RequireSpecialCharacter", true);
        config.Security.KdfIterations = configuration.GetValue<int>("SecuritySettings:KdfIterations", 100000);
        config.Security.SecureDeletePasses = configuration.GetValue<int>("SecuritySettings:SecureDeletePasses", 3);
        config.Security.AuditLogEnabled = configuration.GetValue<bool>("SecuritySettings:AuditLogEnabled", true);
        config.Security.AuditLogRetentionDays = configuration.GetValue<int>("SecuritySettings:AuditLogRetentionDays", 90);

        config.FileSystem.OutputFolder = configuration.GetValue<string>("FileSystem:OutputFolder") ??
            Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "C:\\", "ShamirsSecret");

        config.Organization.Name = configuration.GetValue<string>("Organization:Name") ?? string.Empty;
        config.Organization.ContactPhone = configuration.GetValue<string>("Organization:ContactPhone") ?? string.Empty;

        var defaultKeepers = configuration.GetSection("DefaultKeepers").Get<List<DefaultKeeperSettings>>();
        if (defaultKeepers != null)
        {
            config.DefaultKeepers = defaultKeepers;
        }

        return config;
    }
}

public class SecuritySettings
{
    public bool ConfirmationRequired { get; set; } = true;
    public int MinPasswordLength { get; set; } = 12;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSpecialCharacter { get; set; } = true;
    public int KdfIterations { get; set; } = 100000;
    public int SecureDeletePasses { get; set; } = 3;
    public bool AuditLogEnabled { get; set; } = true;
    public int AuditLogRetentionDays { get; set; } = 90;
}

public class FileSystemSettings
{
    public string OutputFolder { get; set; } = string.Empty;
}

public class OrganizationSettings
{
    public string Name { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
}

public class DefaultKeeperSettings
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int PreferredOrder { get; set; }
}
