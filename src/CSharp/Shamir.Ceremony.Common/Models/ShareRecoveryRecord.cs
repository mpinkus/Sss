namespace Shamir.Ceremony.Common.Models;

public class ShareRecoveryRecord
{
    public DateTime Timestamp { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string OriginalSessionId { get; set; } = string.Empty;
    public int TotalShares { get; set; }
    public int ThresholdRequired { get; set; }
    public List<string> KeepersUsed { get; set; } = new();
    public bool Success { get; set; }
    public string RecoveredSecretHash { get; set; } = string.Empty;
}
