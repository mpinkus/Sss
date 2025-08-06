namespace Shamir.Ceremony.Common.Models;

public class SecretKeeperRecord
{
    public string Id { get; set; } = string.Empty;
    public int ShareNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EncryptedShare { get; set; } = string.Empty;
    public string Hmac { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string IV { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string SessionId { get; set; } = string.Empty;
}
