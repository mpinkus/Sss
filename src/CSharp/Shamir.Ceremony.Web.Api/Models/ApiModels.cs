using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Web.Api.Models;

public class CreateSharesRequest
{
    public int Threshold { get; set; }
    public int TotalShares { get; set; }
    public string? Secret { get; set; }
    public bool GenerateRandomSecret { get; set; } = true;
    public OrganizationInfo? Organization { get; set; }
    public List<KeeperInfo> Keepers { get; set; } = new();
}

public class ReconstructSecretRequest
{
    public string SharesFilePath { get; set; } = string.Empty;
    public List<ShareInput> Shares { get; set; } = new();
}

public class ShareInput
{
    public int ShareNumber { get; set; }
    public string KeeperName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class KeeperInfo
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CeremonyResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public object? Data { get; set; }
}

public class SessionStatusResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public List<string> Events { get; set; } = new();
}
