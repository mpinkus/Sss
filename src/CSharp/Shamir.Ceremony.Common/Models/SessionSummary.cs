namespace Shamir.Ceremony.Common.Models;

public class SessionSummary
{
    public int TotalSharesCreated { get; set; }
    public int TotalShareSets { get; set; }
    public int TotalRecoveryAttempts { get; set; }
    public int SuccessfulRecoveries { get; set; }
    public int FailedRecoveries { get; set; }
    public int TotalEvents { get; set; }
}
