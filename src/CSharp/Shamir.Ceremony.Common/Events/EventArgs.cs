using System.Security;
using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Events;

public class ProgressEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public int? PercentComplete { get; set; }
    public string EventType { get; set; } = string.Empty;
}

public class ValidationEventArgs : EventArgs
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ValidationTarget { get; set; } = string.Empty;
}

public class CompletionEventArgs : EventArgs
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public object? Result { get; set; }
}

public class InputRequestEventArgs : EventArgs
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public InputRequestType RequestType { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    public int MaxLength { get; set; }
    public string ExpectedExtension { get; set; } = string.Empty;
    public Func<string, bool>? Validator { get; set; }
    public TaskCompletionSource<object> CompletionSource { get; set; } = new();
}

public enum InputRequestType
{
    Text,
    SecureString,
    Integer,
    FilePath,
    YesNo
}

public class CeremonyResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputFilePath { get; set; }
    public ShamirSecretOutput? SharesData { get; set; }
    public byte[]? ReconstructedSecret { get; set; }
}
