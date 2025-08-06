namespace Shamir.Ceremony.Common.Models;

public class SessionEvent
{
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
