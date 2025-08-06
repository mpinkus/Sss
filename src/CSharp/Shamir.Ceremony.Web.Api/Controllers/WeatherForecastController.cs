using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Shamir.Ceremony.Web.Api.Hubs;
using Shamir.Ceremony.Web.Api.Models;
using Shamir.Ceremony.Web.Api.Services;

namespace Shamir.Ceremony.Web.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CeremonyController : ControllerBase
    {
        private readonly CeremonyService _ceremonyService;
        private readonly IHubContext<CeremonyHub> _hubContext;
        private readonly ILogger<CeremonyController> _logger;

        public CeremonyController(
            CeremonyService ceremonyService,
            IHubContext<CeremonyHub> hubContext,
            ILogger<CeremonyController> logger)
        {
            _ceremonyService = ceremonyService;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpPost("create-shares")]
        public async Task<ActionResult<CeremonyResponse>> CreateShares([FromBody] CreateSharesRequest request)
        {
            try
            {
                if (request.Threshold > request.TotalShares)
                {
                    return BadRequest(new CeremonyResponse
                    {
                        Success = false,
                        Message = "Threshold cannot be greater than total shares"
                    });
                }

                if (request.Keepers?.Count != request.TotalShares)
                {
                    return BadRequest(new CeremonyResponse
                    {
                        Success = false,
                        Message = "Number of keepers must match total shares"
                    });
                }

                var sessionId = Guid.NewGuid().ToString("N")[..16];
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _ceremonyService.CreateSharesAsync(request, CancellationToken.None, sessionId);
                        
                        await _hubContext.Clients.All.SendAsync("CeremonyCompleted", new
                        {
                            Type = "CREATE_SHARES",
                            Success = result.Success,
                            Message = result.Message,
                            SessionId = result.SessionId
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background ceremony failed for session {SessionId}", sessionId);
                        
                        await _hubContext.Clients.All.SendAsync("CeremonyCompleted", new
                        {
                            Type = "CREATE_SHARES",
                            Success = false,
                            Message = ex.Message,
                            SessionId = sessionId
                        });
                    }
                });

                return Accepted(new CeremonyResponse
                {
                    Success = true,
                    Message = "Ceremony started successfully",
                    SessionId = sessionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting create shares ceremony");
                return StatusCode(500, new CeremonyResponse
                {
                    Success = false,
                    Message = "An error occurred while starting the ceremony"
                });
            }
        }

        [HttpPost("reconstruct-secret")]
        public async Task<ActionResult<CeremonyResponse>> ReconstructSecret([FromBody] ReconstructSecretRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.SharesFilePath))
                {
                    return BadRequest(new CeremonyResponse
                    {
                        Success = false,
                        Message = "Shares file path is required"
                    });
                }

                var sessionId = Guid.NewGuid().ToString("N")[..16];
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _ceremonyService.ReconstructSecretAsync(request, CancellationToken.None, sessionId);
                        
                        await _hubContext.Clients.All.SendAsync("CeremonyCompleted", new
                        {
                            Type = "RECONSTRUCT_SECRET",
                            Success = result.Success,
                            Message = result.Message,
                            SessionId = result.SessionId
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background ceremony failed for session {SessionId}", sessionId);
                        
                        await _hubContext.Clients.All.SendAsync("CeremonyCompleted", new
                        {
                            Type = "RECONSTRUCT_SECRET",
                            Success = false,
                            Message = ex.Message,
                            SessionId = sessionId
                        });
                    }
                });

                return Accepted(new CeremonyResponse
                {
                    Success = true,
                    Message = "Reconstruction started successfully",
                    SessionId = sessionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting reconstruct secret ceremony");
                return StatusCode(500, new CeremonyResponse
                {
                    Success = false,
                    Message = "An error occurred while starting the reconstruction"
                });
            }
        }

        [HttpGet("session/{sessionId}/status")]
        public async Task<ActionResult<SessionStatusResponse>> GetSessionStatus(string sessionId)
        {
            try
            {
                var status = await _ceremonyService.GetSessionStatusAsync(sessionId);
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session status for {SessionId}", sessionId);
                return StatusCode(500, new { Message = "Error retrieving session status" });
            }
        }
    }
}
