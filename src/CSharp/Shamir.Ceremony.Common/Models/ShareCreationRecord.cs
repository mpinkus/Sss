namespace Shamir.Ceremony.Common.Models;

public class ShareCreationRecord
{
    public DateTime Timestamp { get; set; }
    public string OutputFile { get; set; } = string.Empty;
    public int TotalShares { get; set; }
    public int ThresholdRequired { get; set; }
    public string SecretSource { get; set; } = string.Empty;
    public string MasterSecretHash { get; set; } = string.Empty;
    public List<string> KeeperNames { get; set; } = new();
    public bool ConfirmationTestPassed { get; set; }
    public List<string> DefaultKeepersUsed { get; set; } = new();
}
