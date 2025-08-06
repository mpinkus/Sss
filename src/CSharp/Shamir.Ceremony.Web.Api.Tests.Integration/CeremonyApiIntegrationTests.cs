using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Models;
using Shamir.Ceremony.Common.Models;
using Testcontainers.MongoDb;

namespace Shamir.Ceremony.Web.Api.Tests.Integration;

[TestClass]
public class CeremonyApiIntegrationTests
{
    private static MongoDbContainer? _mongoContainer;
    private static WebApplicationFactory<Program>? _factory;
    private static HttpClient? _client;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _mongoContainer = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .WithPortBinding(27017, true)
            .Build();

        await _mongoContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<MongoDbSettings>(options =>
                    {
                        options.ConnectionString = _mongoContainer.GetConnectionString();
                        options.DatabaseName = "ShamirCeremonyTest";
                        options.CollectionName = "KeyValueStoreTest";
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
        
        if (_mongoContainer != null)
        {
            await _mongoContainer.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateShares_WithValidRequest_ShouldReturnAccepted()
    {
        var request = new CreateSharesRequest
        {
            Threshold = 2,
            TotalShares = 3,
            GenerateRandomSecret = true,
            Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
            {
                Name = "Test Organization",
                ContactPhone = "+1-555-0123"
            },
            Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
            {
                new() { Name = "Keeper 1", Phone = "+1-555-0101", Email = "keeper1@test.com", Password = "SecurePass1!" },
                new() { Name = "Keeper 2", Phone = "+1-555-0102", Email = "keeper2@test.com", Password = "SecurePass2!" },
                new() { Name = "Keeper 3", Phone = "+1-555-0103", Email = "keeper3@test.com", Password = "SecurePass3!" }
            }
        };

        var response = await _client!.PostAsJsonAsync("/api/ceremony/create-shares", request);

        response.Should().BeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<CeremonyResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().NotBeNullOrEmpty();
        
        await Task.Delay(100);
    }

    [TestMethod]
    public async Task CreateShares_WithInvalidThreshold_ShouldReturnBadRequest()
    {
        var request = new CreateSharesRequest
        {
            Threshold = 5,
            TotalShares = 3,
            GenerateRandomSecret = true,
            Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
            {
                Name = "Test Organization",
                ContactPhone = "+1-555-0123"
            },
            Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
            {
                new() { Name = "Keeper 1", Phone = "+1-555-0101", Email = "keeper1@test.com", Password = "SecurePass1!" },
                new() { Name = "Keeper 2", Phone = "+1-555-0102", Email = "keeper2@test.com", Password = "SecurePass2!" },
                new() { Name = "Keeper 3", Phone = "+1-555-0103", Email = "keeper3@test.com", Password = "SecurePass3!" }
            }
        };

        var response = await _client!.PostAsJsonAsync("/api/ceremony/create-shares", request);

        response.Should().HaveClientError();
    }

    [TestMethod]
    public async Task GetSessionStatus_WithValidSessionId_ShouldReturnStatus()
    {
        var sessionId = "test-session-123";
        
        using var scope = _factory!.Services.CreateScope();
        var keyValueStore = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();
        
        var sessionState = new
        {
            Status = "COMPLETED",
            ProgressPercentage = 100,
            CurrentStep = "Test completed",
            Events = new List<string> { "Test event" },
            LastUpdated = DateTime.UtcNow
        };
        
        await keyValueStore.SetAsync($"session:{sessionId}", sessionState);

        var response = await _client!.GetAsync($"/api/ceremony/session/{sessionId}/status");

        response.Should().BeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<SessionStatusResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.Status.Should().Be("COMPLETED");
        result.ProgressPercentage.Should().Be(100);
    }

    [TestMethod]
    public async Task GetSessionStatus_WithInvalidSessionId_ShouldReturnNotFound()
    {
        var sessionId = "non-existent-session";

        var response = await _client!.GetAsync($"/api/ceremony/session/{sessionId}/status");

        response.Should().BeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<SessionStatusResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("NOT_FOUND");
    }

    [TestMethod]
    public async Task MongoDbKeyValueStore_ShouldPersistData()
    {
        using var scope = _factory!.Services.CreateScope();
        var keyValueStore = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

        var testData = new { Message = "Test data", Timestamp = DateTime.UtcNow };
        var key = "test-key-" + Guid.NewGuid();

        await keyValueStore.SetAsync(key, testData);
        var exists = await keyValueStore.ExistsAsync(key);
        exists.Should().BeTrue();

        var retrieved = await keyValueStore.GetAsync<object>(key);
        retrieved.Should().NotBeNull();

        var deleted = await keyValueStore.DeleteAsync(key);
        deleted.Should().BeTrue();

        var existsAfterDelete = await keyValueStore.ExistsAsync(key);
        existsAfterDelete.Should().BeFalse();
    }

    [TestMethod]
    public async Task MongoDbKeyValueStore_WithExpiry_ShouldExpireData()
    {
        using var scope = _factory!.Services.CreateScope();
        var keyValueStore = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

        var testData = new { Message = "Expiring data" };
        var key = "expiring-key-" + Guid.NewGuid();

        await keyValueStore.SetAsync(key, testData, TimeSpan.FromSeconds(1));
        
        var retrieved = await keyValueStore.GetAsync<object>(key);
        retrieved.Should().NotBeNull();

        await Task.Delay(TimeSpan.FromSeconds(2));

        var retrievedAfterExpiry = await keyValueStore.GetAsync<object>(key);
        retrievedAfterExpiry.Should().BeNull();
    }
}
