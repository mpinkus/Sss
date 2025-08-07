# Shamir's Secret Sharing Common Library Tests

Comprehensive test suite for the Shamir.Ceremony.Common library covering unit tests, integration tests, and validation scenarios.

## Test Structure

### Unit Tests
- `CeremonyManagerComprehensiveTests.cs`: Core ceremony manager functionality
- `CryptographyServiceComprehensiveTests.cs`: Cryptographic operations
- `ValidationHelperTests.cs`: Input validation logic
- `ConfigurationValidationTests.cs`: Configuration validation rules

### Integration Tests
- End-to-end ceremony workflows
- Database integration scenarios
- File system operations
- Cross-component interactions

## Running Tests

### All Tests
```bash
dotnet test
```

### Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~CeremonyManagerComprehensiveTests"
```

### With Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Test Categories

### Cryptography Tests
- Shamir's Secret Sharing algorithm validation
- GF256 field arithmetic verification
- Encryption/decryption round-trip tests
- Key derivation function validation

### Configuration Tests
- FluentValidation rule verification
- Invalid configuration handling
- Default value validation
- Environment variable integration

### Session Management Tests
- Session lifecycle management
- Event tracking and retrieval
- Session persistence
- Concurrent session handling

### Audit Logging Tests
- Audit trail generation
- Log retention policies
- Log format validation
- Security event logging

## Test Data

### Sample Configurations
```csharp
public static CeremonyConfiguration ValidConfiguration => new()
{
    Security = new SecuritySettings
    {
        MinPasswordLength = 12,
        KdfIterations = 100000,
        AuditLogEnabled = true
    },
    // ... other settings
};
```

### Mock Services
- `MockInputHandler`: Simulated user input
- `MockKeyValueStore`: In-memory storage
- `MockStructuredLogger`: Test logging capture

## Assertions and Validation

### Cryptographic Assertions
```csharp
[TestMethod]
public void ShamirSecretShare_ReconstructionMatchesOriginal()
{
    // Arrange
    var secret = "test-secret";
    var shares = ShamirSecretShare.GenerateShares(secret, 3, 5);
    
    // Act
    var reconstructed = ShamirSecretShare.ReconstructSecret(shares.Take(3).ToList(), 3);
    
    // Assert
    Assert.AreEqual(secret, Encoding.UTF8.GetString(reconstructed));
}
```

### Configuration Validation
```csharp
[TestMethod]
public void SecuritySettings_InvalidPasswordLength_FailsValidation()
{
    // Arrange
    var settings = new SecuritySettings { MinPasswordLength = 4 };
    var validator = new SecuritySettingsValidator();
    
    // Act
    var result = validator.Validate(settings);
    
    // Assert
    Assert.IsFalse(result.IsValid);
    Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(SecuritySettings.MinPasswordLength)));
}
```

### Event Testing
```csharp
[TestMethod]
public async Task CeremonyManager_ProgressEvents_AreRaised()
{
    // Arrange
    var progressEvents = new List<ProgressEventArgs>();
    _ceremonyManager.ProgressUpdated += (s, e) => progressEvents.Add(e);
    
    // Act
    await _ceremonyManager.CreateSharesAsync();
    
    // Assert
    Assert.IsTrue(progressEvents.Count > 0);
    Assert.IsTrue(progressEvents.Any(e => e.EventType == "CREATE_START"));
}
```

## Test Utilities

### Helper Methods
```csharp
public static class TestHelpers
{
    public static CeremonyConfiguration CreateValidConfiguration()
    {
        // Returns valid test configuration
    }
    
    public static List<KeeperInfo> CreateTestKeepers(int count)
    {
        // Returns test keeper data
    }
}
```

### Custom Assertions
```csharp
public static class CryptographyAssert
{
    public static void SharesAreValid(List<Share> shares, int threshold)
    {
        Assert.IsTrue(shares.Count >= threshold);
        Assert.IsTrue(shares.All(s => !string.IsNullOrEmpty(s.Y)));
    }
}
```

## Performance Tests

### Benchmark Tests
```csharp
[TestMethod]
public void ShamirSecretShare_LargeSecret_PerformanceAcceptable()
{
    var largeSecret = new string('A', 10000);
    var stopwatch = Stopwatch.StartNew();
    
    var shares = ShamirSecretShare.GenerateShares(largeSecret, 5, 10);
    
    stopwatch.Stop();
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, "Share generation took too long");
}
```

### Memory Tests
- Memory leak detection
- Secure memory clearing validation
- Large data handling

## Security Tests

### Cryptographic Security
- Random number generation quality
- Key derivation strength
- Encryption algorithm validation

### Input Validation Security
- SQL injection prevention
- Path traversal protection
- Buffer overflow prevention

## Continuous Integration

### Test Execution
Tests are automatically executed on:
- Pull request creation
- Merge to main branch
- Nightly builds

### Coverage Requirements
- Minimum 80% code coverage
- 100% coverage for cryptographic functions
- All public API methods tested

## Debugging Tests

### Visual Studio
1. Set breakpoints in test methods
2. Right-click test → Debug Test
3. Step through code execution

### Command Line
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Test Output
```csharp
[TestMethod]
public void TestWithOutput()
{
    Console.WriteLine("Debug information");
    TestContext.WriteLine("Test-specific output");
}
```

## Test Data Management

### Temporary Files
```csharp
[TestCleanup]
public void Cleanup()
{
    if (Directory.Exists(_tempDirectory))
    {
        Directory.Delete(_tempDirectory, true);
    }
}
```

### Database Cleanup
```csharp
[TestInitialize]
public async Task Setup()
{
    await _database.ClearCollectionAsync("test_ceremonies");
}
```

## Best Practices

1. **Arrange-Act-Assert**: Clear test structure
2. **Descriptive Names**: Test names describe the scenario
3. **Independent Tests**: No dependencies between tests
4. **Fast Execution**: Tests complete quickly
5. **Deterministic**: Tests produce consistent results
6. **Comprehensive**: Cover happy path and edge cases
