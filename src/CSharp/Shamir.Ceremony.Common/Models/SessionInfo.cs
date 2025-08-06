namespace Shamir.Ceremony.Common.Models;

public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public OrganizationInfo Organization { get; set; } = new();
    public SessionSummary Summary { get; set; } = new();
    public List<SessionEvent> Events { get; set; } = new();
    public List<ShareCreationRecord> SharesCreated { get; set; } = new();
    public List<ShareRecoveryRecord> SharesRecovered { get; set; } = new();
}
