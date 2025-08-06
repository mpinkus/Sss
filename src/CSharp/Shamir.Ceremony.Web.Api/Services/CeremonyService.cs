using System.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Hubs;
using Shamir.Ceremony.Web.Api.Models;

namespace Shamir.Ceremony.Web.Api.Services;

public class CeremonyService
{
    private readonly IKeyValueStore _keyValueStore;
    private readonly IHubContext<CeremonyHub> _hubContext;
    private readonly ILogger<CeremonyService> _logger;
    private readonly CeremonyConfiguration _configuration;

    public CeremonyService(
        IKeyValueStore keyValueStore,
        IHubContext<CeremonyHub> hubContext,
        ILogger<CeremonyService> logger,
        IConfiguration configuration)
    {
        _keyValueStore = keyValueStore;
        _hubContext = hubContext;
        _logger = logger;
        _configuration = CeremonyConfiguration.FromConfiguration(configuration);
    }

    public async Task<CeremonyServiceResult> CreateSharesAsync(CreateSharesRequest request, CancellationToken cancellationToken, string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString("N")[..16];
        
        try
        {
            await StoreSessionStateAsync(sessionId, "INITIALIZING", 0, "Starting ceremony");

            var ceremonyManager = new CeremonyManager(_configuration);
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
            _logger.LogError(ex, "Error in CreateSharesAsync for session {SessionId}", sessionId);
            await StoreSessionStateAsync(sessionId, "FAILED", 0, ex.Message);
            
            return new CeremonyServiceResult
            {
                Success = false,
                Message = ex.Message,
                SessionId = sessionId
            };
        }
    }

    public async Task<CeremonyServiceResult> ReconstructSecretAsync(ReconstructSecretRequest request, CancellationToken cancellationToken, string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString("N")[..16];
        
        try
        {
            await StoreSessionStateAsync(sessionId, "INITIALIZING", 0, "Starting reconstruction");

            var ceremonyManager = new CeremonyManager(_configuration);
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
            _logger.LogError(ex, "Error in ReconstructSecretAsync for session {SessionId}", sessionId);
            await StoreSessionStateAsync(sessionId, "FAILED", 0, ex.Message);
            
            return new CeremonyServiceResult
            {
                Success = false,
                Message = ex.Message,
                SessionId = sessionId
            };
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
