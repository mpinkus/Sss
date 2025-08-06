using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common.Services;

public class SessionManager
{
    private readonly SessionInfo _sessionInfo;
    private readonly string _sessionOutputFolder;
    private readonly byte[] _adminSessionKey;
    private readonly CryptographyService _cryptographyService;

    public SessionManager(string sessionId, string outputFolder, byte[] adminSessionKey, CryptographyService cryptographyService)
    {
        _sessionInfo = new SessionInfo
        {
            SessionId = sessionId,
            StartTime = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            ApplicationVersion = "1.0.0",
            OutputFolder = outputFolder,
            Events = new List<SessionEvent>(),
            SharesCreated = new List<ShareCreationRecord>(),
            SharesRecovered = new List<ShareRecoveryRecord>()
        };

        _sessionOutputFolder = outputFolder;
        _adminSessionKey = adminSessionKey;
        _cryptographyService = cryptographyService;
    }

    public SessionInfo SessionInfo => _sessionInfo;

    public void AddEvent(string eventType, string description)
    {
        _sessionInfo.Events.Add(new SessionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Description = description
        });
    }

    public void AddShareCreationRecord(ShareCreationRecord record)
    {
        _sessionInfo.SharesCreated.Add(record);
    }

    public void AddShareRecoveryRecord(ShareRecoveryRecord record)
    {
        _sessionInfo.SharesRecovered.Add(record);
    }

    public void SaveSessionInfo()
    {
        _sessionInfo.EndTime = DateTime.UtcNow;
        _sessionInfo.Duration = _sessionInfo.EndTime - _sessionInfo.StartTime;

        _sessionInfo.Summary = new SessionSummary
        {
            TotalSharesCreated = _sessionInfo.SharesCreated.Sum(s => s.TotalShares),
            TotalShareSets = _sessionInfo.SharesCreated.Count,
            TotalRecoveryAttempts = _sessionInfo.SharesRecovered.Count,
            SuccessfulRecoveries = _sessionInfo.SharesRecovered.Count(r => r.Success),
            FailedRecoveries = _sessionInfo.SharesRecovered.Count(r => !r.Success),
            TotalEvents = _sessionInfo.Events.Count
        };

        AddEvent("SESSION_END", $"Session completed after {_sessionInfo.Duration.TotalMinutes:F2} minutes");

        var sessionJson = JsonSerializer.Serialize(_sessionInfo, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        string sessionHmac = "";
        if (_adminSessionKey != null)
        {
            using var hmac = new HMACSHA256(_adminSessionKey);
            var sessionBytes = Encoding.UTF8.GetBytes(sessionJson);
            var hmacBytes = hmac.ComputeHash(sessionBytes);
            sessionHmac = Convert.ToBase64String(hmacBytes);
        }

        var sessionOutput = new SessionOutput
        {
            SessionData = _sessionInfo,
            SessionDataHash = _cryptographyService.CalculateSha256Hash(sessionJson),
            AdminSessionHmac = sessionHmac,
            HmacAlgorithm = "HMAC-SHA256",
            SignatureTimestamp = DateTime.UtcNow,
            SignatureNote = "This HMAC signature provides non-repudiation and proves administrator oversight of this session. Verify with the administrator's session password."
        };

        var finalJson = JsonSerializer.Serialize(sessionOutput, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        string sessionFilename = $"session_complete_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string fullSessionPath = Path.Combine(_sessionOutputFolder, sessionFilename);

        try
        {
            File.WriteAllText(fullSessionPath, finalJson);
            CreateSessionReadme();
        }
        catch (Exception)
        {
        }
    }

    private void CreateSessionReadme()
    {
        try
        {
            var readme = new StringBuilder();
            readme.AppendLine("SHAMIR'S SECRET SHARING SESSION");
            readme.AppendLine("================================");
            readme.AppendLine();
            readme.AppendLine($"Session ID: {_sessionInfo.SessionId}");
            readme.AppendLine($"Date: {_sessionInfo.StartTime:yyyy-MM-dd}");
            readme.AppendLine($"Duration: {_sessionInfo.Duration.TotalMinutes:F2} minutes");
            readme.AppendLine($"Machine: {_sessionInfo.MachineName}");
            readme.AppendLine($"User: {_sessionInfo.UserName}");
            readme.AppendLine();

            if (_sessionInfo.Organization != null)
            {
                readme.AppendLine("ORGANIZATION");
                readme.AppendLine("------------");
                readme.AppendLine($"Name: {_sessionInfo.Organization.Name}");
                readme.AppendLine($"Contact: {_sessionInfo.Organization.ContactPhone}");
                readme.AppendLine();
            }

            readme.AppendLine("SESSION SUMMARY");
            readme.AppendLine("---------------");
            readme.AppendLine($"Share Sets Created: {_sessionInfo.Summary.TotalShareSets}");
            readme.AppendLine($"Total Shares: {_sessionInfo.Summary.TotalSharesCreated}");
            readme.AppendLine($"Recovery Attempts: {_sessionInfo.Summary.TotalRecoveryAttempts}");
            readme.AppendLine($"Successful Recoveries: {_sessionInfo.Summary.SuccessfulRecoveries}");
            readme.AppendLine();

            readme.AppendLine("FILES IN THIS SESSION");
            readme.AppendLine("---------------------");

            var files = Directory.GetFiles(_sessionOutputFolder, "*.*");
            foreach (var file in files.OrderBy(f => f))
            {
                var fileInfo = new FileInfo(file);
                readme.AppendLine($"- {fileInfo.Name} ({fileInfo.Length:N0} bytes)");
            }

            readme.AppendLine();
            readme.AppendLine("IMPORTANT NOTES");
            readme.AppendLine("---------------");
            readme.AppendLine("• Keep all session files together for audit purposes");
            readme.AppendLine("• The session_complete_*.json file contains cryptographic proof of the session");
            readme.AppendLine("• Distribute secret_shares_*.json files to appropriate keepers");
            readme.AppendLine("• Store audit logs securely for compliance purposes");

            string readmePath = Path.Combine(_sessionOutputFolder, "README.txt");
            File.WriteAllText(readmePath, readme.ToString());
        }
        catch (Exception)
        {
        }
    }
}
