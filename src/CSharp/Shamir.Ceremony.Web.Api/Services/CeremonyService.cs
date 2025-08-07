using System.Diagnostics;
using System.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Prometheus;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Hubs;
using Shamir.Ceremony.Web.Api.Models;

namespace Shamir.Ceremony.Web.Api.Services;

public class CeremonyService
{
    private readonly IKeyValueStore _keyValueStore;
    private readonly IHubContext<CeremonyHub> _hubContext;
    private readonly ILogger<CeremonyService> _logger;
    private readonly IStructuredLogger _structuredLogger;
    private readonly CeremonyConfiguration _configuration;
    
    private static readonly Counter CeremoniesTotal = Metrics
        .CreateCounter("ceremonies_total", "Total number of ceremonies", new[] { "type", "result" });
    
    private static readonly Histogram CeremonyDuration = Metrics
        .CreateHistogram("ceremony_duration_seconds", "Duration of ceremonies", new[] { "type" });
    
    private static readonly Gauge ActiveSessions = Metrics
        .CreateGauge("active_sessions", "Number of active ceremony sessions");

    public CeremonyService(
        IKeyValueStore keyValueStore,
        IHubContext<CeremonyHub> hubContext,
        ILogger<CeremonyService> logger,
        IStructuredLogger structuredLogger,
        IConfiguration configuration)
    {
        _keyValueStore = keyValueStore;
        _hubContext = hubContext;
        _logger = logger;
        _structuredLogger = structuredLogger;
        _configuration = CeremonyConfiguration.FromConfiguration(configuration);
    }

    public async Task<CeremonyServiceResult> CreateSharesAsync(CreateSharesRequest request, CancellationToken cancellationToken, string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString("N")[..16];
        var stopwatch = Stopwatch.StartNew();
        
        ActiveSessions.Inc();
        _structuredLogger.LogCeremonyStart("CREATE_SHARES", sessionId);
        
        try
        {
            await StoreSessionStateAsync(sessionId, "INITIALIZING", 0, "Starting ceremony");

            var ceremonyManager = new CeremonyManager(_configuration, _structuredLogger);
            var inputHandler = new WebInputHandler(request, _hubContext, _keyValueStore, sessionId);
            
            ceremonyManager.ProgressUpdated += async (sender, e) =>
            {
                await _hubContext.Clients.All.SendAsync("ProgressUpdate", new
                {
                    SessionId = sessionId,
                    Message = e.Message,
                    PercentComplete = e.PercentComplete,
                    EventType = e.EventType
                }, cancellationToken);

                await StoreSessionStateAsync(sessionId, e.EventType, e.PercentComplete ?? 0, e.Message);
            };

            ceremonyManager.InputRequested += inputHandler.HandleInputRequest;
            ceremonyManager.ValidationResult += async (sender, e) =>
            {
                await _hubContext.Clients.All.SendAsync("ValidationResult", new
                {
                    SessionId = sessionId,
                    IsValid = e.IsValid,
                    Message = e.Message,
                    ValidationTarget = e.ValidationTarget
                }, cancellationToken);
            };

            var result = await ceremonyManager.CreateSharesAsync();
            ceremonyManager.FinalizeSession();

            stopwatch.Stop();
            CeremonyDuration.WithLabels("create_shares").Observe(stopwatch.Elapsed.TotalSeconds);
            CeremoniesTotal.WithLabels("create_shares", result.Success ? "success" : "failure").Inc();
            _structuredLogger.LogCeremonyComplete("CREATE_SHARES", sessionId, result.Success, stopwatch.Elapsed);

            await StoreSessionStateAsync(sessionId, result.Success ? "COMPLETED" : "FAILED", 100, result.Message);

            return new CeremonyServiceResult
            {
                Success = result.Success,
                Message = result.Message,
                SessionId = sessionId,
                SharesData = result.SharesData
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            CeremonyDuration.WithLabels("create_shares").Observe(stopwatch.Elapsed.TotalSeconds);
            CeremoniesTotal.WithLabels("create_shares", "failure").Inc();
            _structuredLogger.LogException(ex, "CreateSharesAsync", sessionId);
            _logger.LogError(ex, "Error in CreateSharesAsync for session {SessionId}", sessionId);
            await StoreSessionStateAsync(sessionId, "FAILED", 0, ex.Message);
            
            return new CeremonyServiceResult
            {
                Success = false,
                Message = ex.Message,
                SessionId = sessionId
            };
        }
        finally
        {
            ActiveSessions.Dec();
        }
    }

    public async Task<CeremonyServiceResult> ReconstructSecretAsync(ReconstructSecretRequest request, CancellationToken cancellationToken, string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString("N")[..16];
        var stopwatch = Stopwatch.StartNew();
        
        ActiveSessions.Inc();
        _structuredLogger.LogCeremonyStart("RECONSTRUCT_SECRET", sessionId);
        
        try
        {
            await StoreSessionStateAsync(sessionId, "INITIALIZING", 0, "Starting reconstruction");

            var ceremonyManager = new CeremonyManager(_configuration, _structuredLogger);
            var inputHandler = new WebInputHandler(request, _hubContext, _keyValueStore, sessionId);
            
            ceremonyManager.ProgressUpdated += async (sender, e) =>
            {
                await _hubContext.Clients.All.SendAsync("ProgressUpdate", new
                {
                    SessionId = sessionId,
                    Message = e.Message,
                    PercentComplete = e.PercentComplete,
                    EventType = e.EventType
                }, cancellationToken);

                await StoreSessionStateAsync(sessionId, e.EventType, e.PercentComplete ?? 0, e.Message);
            };

            ceremonyManager.InputRequested += inputHandler.HandleInputRequest;
            ceremonyManager.ValidationResult += async (sender, e) =>
            {
                await _hubContext.Clients.All.SendAsync("ValidationResult", new
                {
                    SessionId = sessionId,
                    IsValid = e.IsValid,
                    Message = e.Message,
                    ValidationTarget = e.ValidationTarget
                }, cancellationToken);
            };

            var result = await ceremonyManager.ReconstructSecretAsync(request.SharesFilePath);
            ceremonyManager.FinalizeSession();

            stopwatch.Stop();
            CeremonyDuration.WithLabels("reconstruct_secret").Observe(stopwatch.Elapsed.TotalSeconds);
            CeremoniesTotal.WithLabels("reconstruct_secret", result.Success ? "success" : "failure").Inc();
            _structuredLogger.LogCeremonyComplete("RECONSTRUCT_SECRET", sessionId, result.Success, stopwatch.Elapsed);

            await StoreSessionStateAsync(sessionId, result.Success ? "COMPLETED" : "FAILED", 100, result.Message);

            return new CeremonyServiceResult
            {
                Success = result.Success,
                Message = result.Message,
                SessionId = sessionId,
                ReconstructedSecret = result.ReconstructedSecret
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            CeremonyDuration.WithLabels("reconstruct_secret").Observe(stopwatch.Elapsed.TotalSeconds);
            CeremoniesTotal.WithLabels("reconstruct_secret", "failure").Inc();
            _structuredLogger.LogException(ex, "ReconstructSecretAsync", sessionId);
            _logger.LogError(ex, "Error in ReconstructSecretAsync for session {SessionId}", sessionId);
            await StoreSessionStateAsync(sessionId, "FAILED", 0, ex.Message);
            
            return new CeremonyServiceResult
            {
                Success = false,
                Message = ex.Message,
                SessionId = sessionId
            };
        }
        finally
        {
            ActiveSessions.Dec();
        }
    }

    public async Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId)
    {
        var status = await _keyValueStore.GetAsync<SessionState>($"session:{sessionId}");
        
        if (status == null)
        {
            return new SessionStatusResponse
            {
                SessionId = sessionId,
                Status = "NOT_FOUND",
                ProgressPercentage = 0,
                CurrentStep = "Unknown",
                Events = new List<string>()
            };
        }

        return new SessionStatusResponse
        {
            SessionId = sessionId,
            Status = status.Status,
            ProgressPercentage = status.ProgressPercentage,
            CurrentStep = status.CurrentStep,
            Events = status.Events
        };
    }

    private async Task StoreSessionStateAsync(string sessionId, string status, int progress, string message)
    {
        var existingState = await _keyValueStore.GetAsync<SessionState>($"session:{sessionId}") ?? new SessionState();
        
        existingState.Status = status;
        existingState.ProgressPercentage = progress;
        existingState.CurrentStep = message;
        existingState.Events.Add($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        existingState.LastUpdated = DateTime.UtcNow;

        await _keyValueStore.SetAsync($"session:{sessionId}", existingState, TimeSpan.FromHours(24));
    }
}

public class CeremonyServiceResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ShamirSecretOutput? SharesData { get; set; }
    public byte[]? ReconstructedSecret { get; set; }
}

public class SessionState
{
    public string Status { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public List<string> Events { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
