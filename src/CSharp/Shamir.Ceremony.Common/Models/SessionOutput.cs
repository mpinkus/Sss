namespace Shamir.Ceremony.Common.Models;

public class SessionOutput
{
    public SessionInfo SessionData { get; set; } = new();
    public string SessionDataHash { get; set; } = string.Empty;
    public string AdminSessionHmac { get; set; } = string.Empty;
    public string HmacAlgorithm { get; set; } = string.Empty;
    public DateTime SignatureTimestamp { get; set; }
    public string SignatureNote { get; set; } = string.Empty;
}
