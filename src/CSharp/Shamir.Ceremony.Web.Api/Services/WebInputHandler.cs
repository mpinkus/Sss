using System.Security;
using Microsoft.AspNetCore.SignalR;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Hubs;
using Shamir.Ceremony.Web.Api.Models;

namespace Shamir.Ceremony.Web.Api.Services;

public class WebInputHandler
{
    private readonly CreateSharesRequest? _createRequest;
    private readonly ReconstructSecretRequest? _reconstructRequest;
    private readonly IHubContext<CeremonyHub> _hubContext;
    private readonly IKeyValueStore _keyValueStore;
    private readonly string _sessionId;
    private int _currentKeeperIndex = 0;

    public WebInputHandler(
        CreateSharesRequest createRequest,
        IHubContext<CeremonyHub> hubContext,
        IKeyValueStore keyValueStore,
        string sessionId)
    {
        _createRequest = createRequest;
        _hubContext = hubContext;
        _keyValueStore = keyValueStore;
        _sessionId = sessionId;
    }

    public WebInputHandler(
        ReconstructSecretRequest reconstructRequest,
        IHubContext<CeremonyHub> hubContext,
        IKeyValueStore keyValueStore,
        string sessionId)
    {
        _reconstructRequest = reconstructRequest;
        _hubContext = hubContext;
        _keyValueStore = keyValueStore;
        _sessionId = sessionId;
    }

    public async void HandleInputRequest(object? sender, InputRequestEventArgs e)
    {
        try
        {
            object result = e.RequestType switch
            {
                InputRequestType.Text => await HandleTextInput(e),
                InputRequestType.SecureString => await HandleSecureStringInput(e),
                InputRequestType.Integer => await HandleIntegerInput(e),
                InputRequestType.YesNo => await HandleYesNoInput(e),
                InputRequestType.FilePath => await HandleFilePathInput(e),
                _ => throw new NotSupportedException($"Input type {e.RequestType} not supported")
            };

            e.CompletionSource.SetResult(result);
        }
        catch (Exception ex)
        {
            e.CompletionSource.SetException(ex);
        }
    }

    private async Task<string> HandleTextInput(InputRequestEventArgs e)
    {
        if (_createRequest != null)
        {
            if (e.Prompt.Contains("name for keeper"))
            {
                if (_currentKeeperIndex < _createRequest.Keepers.Count)
                {
                    return _createRequest.Keepers[_currentKeeperIndex].Name;
                }
            }
            else if (e.Prompt.Contains("phone for"))
            {
                if (_currentKeeperIndex < _createRequest.Keepers.Count)
                {
                    return _createRequest.Keepers[_currentKeeperIndex].Phone;
                }
            }
            else if (e.Prompt.Contains("email for"))
            {
                if (_currentKeeperIndex < _createRequest.Keepers.Count)
                {
                    var result = _createRequest.Keepers[_currentKeeperIndex].Email;
                    _currentKeeperIndex++;
                    return result;
                }
            }
            else if (e.Prompt.Contains("organization name"))
            {
                return _createRequest.Organization?.Name ?? "Default Organization";
            }
            else if (e.Prompt.Contains("organization contact phone"))
            {
                return _createRequest.Organization?.ContactPhone ?? "+1-555-0123";
            }
        }

        await _hubContext.Clients.All.SendAsync("InputRequired", new
        {
            SessionId = _sessionId,
            Type = "Text",
            Prompt = e.Prompt,
            MaxLength = e.MaxLength
        });

        return await WaitForUserInput<string>($"input:{_sessionId}:text");
    }

    private async Task<SecureString> HandleSecureStringInput(InputRequestEventArgs e)
    {
        if (_createRequest != null && e.Prompt.Contains("password for"))
        {
            var keeperName = ExtractKeeperNameFromPrompt(e.Prompt);
            var keeper = _createRequest.Keepers.FirstOrDefault(k => k.Name == keeperName);
            if (keeper != null)
            {
                var secureString = new SecureString();
                foreach (char c in keeper.Password)
                {
                    secureString.AppendChar(c);
                }
                secureString.MakeReadOnly();
                return secureString;
            }
        }
        else if (_createRequest != null && e.Prompt.Contains("Administrator session password"))
        {
            var adminPassword = Environment.GetEnvironmentVariable("CEREMONY_ADMIN_PASSWORD") ?? "DefaultAdminPass123!";
            var secureString = new SecureString();
            foreach (char c in adminPassword)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }

        await _hubContext.Clients.All.SendAsync("InputRequired", new
        {
            SessionId = _sessionId,
            Type = "SecureString",
            Prompt = e.Prompt
        });

        var password = await WaitForUserInput<string>($"input:{_sessionId}:secure");
        var result = new SecureString();
        foreach (char c in password)
        {
            result.AppendChar(c);
        }
        result.MakeReadOnly();
        return result;
    }

    private async Task<int> HandleIntegerInput(InputRequestEventArgs e)
    {
        if (_createRequest != null)
        {
            if (e.Prompt.Contains("minimum number of secret keepers"))
            {
                return _createRequest.Threshold;
            }
            else if (e.Prompt.Contains("total number of secret keepers"))
            {
                return _createRequest.TotalShares;
            }
        }

        await _hubContext.Clients.All.SendAsync("InputRequired", new
        {
            SessionId = _sessionId,
            Type = "Integer",
            Prompt = e.Prompt,
            MinValue = e.MinValue,
            MaxValue = e.MaxValue
        });

        return await WaitForUserInput<int>($"input:{_sessionId}:integer");
    }

    private async Task<bool> HandleYesNoInput(InputRequestEventArgs e)
    {
        if (_createRequest != null)
        {
            if (e.Prompt.Contains("Generate random secret"))
            {
                return _createRequest.GenerateRandomSecret;
            }
            else if (e.Prompt.Contains("Use this organization"))
            {
                return true;
            }
            else if (e.Prompt.Contains("Use") && e.Prompt.Contains("as Secret Keeper"))
            {
                return true;
            }
        }

        await _hubContext.Clients.All.SendAsync("InputRequired", new
        {
            SessionId = _sessionId,
            Type = "YesNo",
            Prompt = e.Prompt
        });

        return await WaitForUserInput<bool>($"input:{_sessionId}:yesno");
    }

    private async Task<string> HandleFilePathInput(InputRequestEventArgs e)
    {
        if (_reconstructRequest != null && !string.IsNullOrEmpty(_reconstructRequest.SharesFilePath))
        {
            return _reconstructRequest.SharesFilePath;
        }

        await _hubContext.Clients.All.SendAsync("InputRequired", new
        {
            SessionId = _sessionId,
            Type = "FilePath",
            Prompt = e.Prompt,
            ExpectedExtension = e.ExpectedExtension
        });

        return await WaitForUserInput<string>($"input:{_sessionId}:filepath");
    }

    private async Task<T> WaitForUserInput<T>(string key)
    {
        var timeout = TimeSpan.FromMinutes(5);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var input = await _keyValueStore.GetAsync<UserInputResponse>(key);
            if (input != null)
            {
                await _keyValueStore.DeleteAsync(key);
                return (T)input.Value;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException("User input timeout");
    }

    private static string ExtractKeeperNameFromPrompt(string prompt)
    {
        var parts = prompt.Split(' ');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "for")
            {
                return parts[i + 1].TrimEnd(':');
            }
        }
        return string.Empty;
    }
}

public class UserInputResponse
{
    public object Value { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
