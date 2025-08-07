using Microsoft.Extensions.Logging;

namespace Shamir.Ceremony.Common.Services;

public interface IStructuredLogger
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(string message, params object[] args);
    void LogError(Exception exception, string message, params object[] args);
    void LogDebug(string message, params object[] args);
    void LogCritical(string message, params object[] args);
    void LogCritical(Exception exception, string message, params object[] args);
    
    void LogCeremonyStart(string ceremonyType, string sessionId);
    void LogCeremonyComplete(string ceremonyType, string sessionId, bool success, TimeSpan duration);
    void LogShareCreation(int totalShares, int threshold, string sessionId);
    void LogSecretReconstruction(int sharesUsed, string sessionId, bool success);
    void LogValidationResult(string validationType, bool isValid, string message);
    void LogException(Exception exception, string context, string sessionId = "");
}
