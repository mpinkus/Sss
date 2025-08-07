using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shamir.Ceremony.Common.Configuration;

namespace Shamir.Ceremony.Web.Api.Services;

public class FileSystemHealthCheck : IHealthCheck
{
    private readonly FileSystemSettings _fileSystemSettings;

    public FileSystemHealthCheck(IOptions<FileSystemSettings> fileSystemSettings)
    {
        _fileSystemSettings = fileSystemSettings.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var outputFolder = _fileSystemSettings.OutputFolder;
            
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var testFile = Path.Combine(outputFolder, $"health_check_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "health check");
            
            if (!File.Exists(testFile))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Cannot create test file in output directory"));
            }

            File.Delete(testFile);
            
            return Task.FromResult(HealthCheckResult.Healthy("File system is accessible"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"File system check failed: {ex.Message}"));
        }
    }
}
