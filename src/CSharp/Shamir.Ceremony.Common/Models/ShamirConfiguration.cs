namespace Shamir.Ceremony.Common.Models;

public class ShamirConfiguration
{
    public int TotalShares { get; set; }
    public int ThresholdRequired { get; set; }
    public string Algorithm { get; set; } = "Shamir-GF256";
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public string KdfAlgorithm { get; set; } = "PBKDF2-SHA256";
    public int KdfIterations { get; set; }
}
