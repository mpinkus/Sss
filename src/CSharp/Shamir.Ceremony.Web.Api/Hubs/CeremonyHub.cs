using Microsoft.AspNetCore.SignalR;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Services;

namespace Shamir.Ceremony.Web.Api.Hubs;

public class CeremonyHub : Hub
{
    private readonly IKeyValueStore _keyValueStore;
    private readonly ILogger<CeremonyHub> _logger;

    public CeremonyHub(IKeyValueStore keyValueStore, ILogger<CeremonyHub> logger)
    {
        _keyValueStore = keyValueStore;
        _logger = logger;
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _logger.LogInformation("Client {ConnectionId} joined session {SessionId}", Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _logger.LogInformation("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
    }

    public async Task ProvideInput(string sessionId, string inputType, object value)
    {
        try
        {
            var key = $"input:{sessionId}:{inputType.ToLower()}";
            var response = new UserInputResponse { Value = value };
            
            await _keyValueStore.SetAsync(key, response, TimeSpan.FromMinutes(10));
            
            _logger.LogInformation("Input provided for session {SessionId}, type {InputType}", sessionId, inputType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error providing input for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("Error", "Failed to provide input");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
