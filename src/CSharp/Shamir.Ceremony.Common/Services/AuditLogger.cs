using System.Text.Json;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Configuration;

namespace Shamir.Ceremony.Common.Services;

public class AuditLogger
{
    private readonly SecuritySettings _securitySettings;
    private readonly List<AuditLogEntry> _auditLog = new();
    private readonly string _auditLogFile;
    private readonly string _sessionId;

    public AuditLogger(SecuritySettings securitySettings, string sessionId, string outputFolder)
    {
        _securitySettings = securitySettings;
        _sessionId = sessionId;
        _auditLogFile = Path.Combine(outputFolder, $"audit_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    public void LogAudit(string eventType, string message)
    {
        if (!_securitySettings.AuditLogEnabled)
            return;

        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            SessionId = _sessionId,
            EventType = eventType,
            Message = message,
            User = Environment.UserName,
            Machine = Environment.MachineName
        };

        _auditLog.Add(entry);

        string logLine = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {entry.SessionId} | {entry.EventType} | {entry.User}@{entry.Machine} | {entry.Message}";
        try
        {
            File.AppendAllText(_auditLogFile, logLine + Environment.NewLine);
        }
        catch
        {
        }
    }

    public void SaveAuditLog(string outputFolder)
    {
        if (!_securitySettings.AuditLogEnabled || _auditLog.Count == 0)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_auditLog, new JsonSerializerOptions { WriteIndented = true });
            string filename = $"audit_detail_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string fullPath = Path.Combine(outputFolder, filename);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception)
        {
        }
    }

    public List<AuditLogEntry> GetAuditEntries() => new(_auditLog);
}
