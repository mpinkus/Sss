using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Models;

public class ShamirSecretOutput
{
    public string Version { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public OrganizationInfo Organization { get; set; } = new();
    public ShamirConfiguration Configuration { get; set; } = new();
    public string MasterSecretHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<SecretKeeperRecord> Keepers { get; set; } = new();
}
