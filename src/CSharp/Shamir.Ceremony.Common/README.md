# Shamir's Secret Sharing Common Library

Core library containing shared components for Shamir's Secret Sharing ceremony implementations.

## Components

### CeremonyManager
Central orchestrator for secret sharing ceremonies with event-driven architecture.

```csharp
var config = CeremonyConfiguration.FromConfiguration(configuration);
var logger = serviceProvider.GetService<IStructuredLogger>();
var manager = new CeremonyManager(config, logger);

// Subscribe to events
manager.ProgressUpdated += OnProgress;
manager.ValidationResult += OnValidation;
manager.OperationCompleted += OnCompletion;

// Perform operations
var result = await manager.CreateSharesAsync();
```

### Configuration System
Strongly-typed configuration with FluentValidation:

- `CeremonyConfiguration`: Root configuration
- `SecuritySettings`: Security parameters
- `FileSystemSettings`: File system configuration
- `OrganizationSettings`: Organization details
- `MongoDbSettings`: Database configuration

### Cryptography Services
Secure cryptographic operations:

- `ShamirSecretShare`: Core secret sharing algorithm
- `CryptographyService`: High-level crypto operations
- `GF256`: Galois Field arithmetic

### Services

#### Structured Logging
```csharp
public interface IStructuredLogger
{
    void LogCeremonyStart(string ceremonyType, string sessionId);
    void LogException(Exception exception, string context, string sessionId);
    // ... other methods
}
```

#### Audit Logging
```csharp
var auditLogger = new AuditLogger(retentionDays: 2555);
auditLogger.LogAudit("CEREMONY_START", "User initiated ceremony");
```

#### Session Management
```csharp
var sessionManager = new SessionManager();
sessionManager.AddEvent("SHARE_CREATED", "Share created for keeper Alice");
```

## Models

### Core Models
- `CeremonyResult`: Operation results
- `ShamirSecretOutput`: Share generation output
- `KeeperInfo`: Keeper details
- `Share`: Individual secret share

### Event Models
- `ProgressEventArgs`: Progress notifications
- `ValidationEventArgs`: Validation results
- `InputRequestEventArgs`: Input requests
- `CompletionEventArgs`: Operation completion

## Configuration Validation

All configuration classes include FluentValidation rules:

```csharp
services.AddSingleton<IValidator<SecuritySettings>, SecuritySettingsValidator>();
services.AddSingleton<IValidator<MongoDbSettings>, MongoDbSettingsValidator>();
// ... other validators
```

### Validation Rules

#### SecuritySettings
- Minimum password length ≥ 8 characters
- KDF iterations ≥ 10,000
- Secure delete passes: 1-10
- Audit log retention: 1-3650 days

#### FileSystemSettings
- Valid output folder path
- Directory accessibility

#### OrganizationSettings
- Required organization name (≤ 200 chars)
- Valid phone number format

## Usage Patterns

### Dependency Injection
```csharp
services.Configure<SecuritySettings>(configuration.GetSection("Security"));
services.AddScoped<IStructuredLogger, StructuredLogger>();
services.AddSingleton<CeremonyManager>();
```

### Event Handling
```csharp
manager.ProgressUpdated += (sender, e) => {
    logger.LogInformation("Progress: {Percent}% - {Message}", 
        e.PercentComplete, e.Message);
};
```

### Error Handling
```csharp
try
{
    var result = await manager.CreateSharesAsync();
}
catch (Exception ex)
{
    structuredLogger.LogException(ex, "CreateShares", sessionId);
    throw;
}
```

## Security Features

- **Memory Security**: Automatic secure deletion of sensitive data
- **Cryptographic Validation**: Input validation for all crypto operations
- **Audit Trail**: Comprehensive logging of all operations
- **Password Complexity**: Configurable password requirements

## Extension Points

### Custom Input Handlers
Implement `IInputHandler` for different UI frameworks:

```csharp
public class WebInputHandler : IInputHandler
{
    public async Task<string> GetInputAsync(string prompt, bool isPassword)
    {
        // Custom implementation
    }
}
```

### Custom Storage
Implement `IKeyValueStore` for different storage backends:

```csharp
public class RedisKeyValueStore : IKeyValueStore
{
    // Redis implementation
}
```

## Testing

The library includes comprehensive test coverage:
- Unit tests for all core components
- Integration tests for end-to-end scenarios
- Cryptographic validation tests
- Configuration validation tests

```bash
dotnet test Shamir.Ceremony.Common.Tests
```

## Dependencies

- .NET 8.0
- MongoDB.Driver (database operations)
- FluentValidation (configuration validation)
- Serilog (structured logging)
- Microsoft.Extensions.* (dependency injection, configuration)
