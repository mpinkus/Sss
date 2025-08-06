namespace Shamir.Ceremony.Common.Models;

public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
}
