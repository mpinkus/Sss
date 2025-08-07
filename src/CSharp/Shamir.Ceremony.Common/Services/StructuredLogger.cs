using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Shamir.Ceremony.Common.Services;

public class StructuredLogger : IStructuredLogger
{
    private readonly ILogger _logger;

    public StructuredLogger(ILogger<StructuredLogger> logger)
    {
        _logger = logger;
    }

    public void LogInformation(string message, params object[] args)
    {
        _logger.LogInformation(message, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        _logger.LogWarning(message, args);
    }

    public void LogError(string message, params object[] args)
    {
        _logger.LogError(message, args);
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
        _logger.LogError(exception, message, args);
    }

    public void LogDebug(string message, params object[] args)
    {
        _logger.LogDebug(message, args);
    }

    public void LogCritical(string message, params object[] args)
    {
        _logger.LogCritical(message, args);
    }

    public void LogCritical(Exception exception, string message, params object[] args)
    {
        _logger.LogCritical(exception, message, args);
    }

    public void LogCeremonyStart(string ceremonyType, string sessionId)
    {
        _logger.LogInformation("Ceremony started: {CeremonyType} with session {SessionId}", ceremonyType, sessionId);
    }

    public void LogCeremonyComplete(string ceremonyType, string sessionId, bool success, TimeSpan duration)
    {
        _logger.LogInformation("Ceremony completed: {CeremonyType} with session {SessionId}, Success: {Success}, Duration: {Duration}ms", 
            ceremonyType, sessionId, success, duration.TotalMilliseconds);
    }

    public void LogShareCreation(int totalShares, int threshold, string sessionId)
    {
        _logger.LogInformation("Shares created: {TotalShares} total, {Threshold} threshold for session {SessionId}", 
            totalShares, threshold, sessionId);
    }

    public void LogSecretReconstruction(int sharesUsed, string sessionId, bool success)
    {
        _logger.LogInformation("Secret reconstruction: {SharesUsed} shares used for session {SessionId}, Success: {Success}", 
            sharesUsed, sessionId, success);
    }

    public void LogValidationResult(string validationType, bool isValid, string message)
    {
        _logger.LogInformation("Validation result: {ValidationType}, Valid: {IsValid}, Message: {Message}", 
            validationType, isValid, message);
    }

    public void LogException(Exception exception, string context, string sessionId = "")
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogError(exception, "Exception in {Context}: {Message}", context, exception.Message);
        }
        else
        {
            _logger.LogError(exception, "Exception in {Context} for session {SessionId}: {Message}", context, sessionId, exception.Message);
        }
    }
}
