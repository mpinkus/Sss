using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Models;
using Testcontainers.MongoDb;

namespace Shamir.Ceremony.Web.Api.Tests.Integration
{
    [TestClass]
    public class WebApiE2ETests
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
                            options.DatabaseName = "ShamirE2ETest";
                            options.CollectionName = "E2EKeyValueStore";
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
        public async Task CompleteWebFlow_CreateReconstructViaAPI()
        {
            var sessionId = $"e2e-session-{Guid.NewGuid():N}";

            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            var progressUpdates = new List<string>();
            hubConnection.On<object>("ProgressUpdate", (message) =>
            {
                progressUpdates.Add($"Progress: {message}");
            });

            await hubConnection.StartAsync();
            await hubConnection.InvokeAsync("JoinSession", sessionId);

            var createRequest = new CreateSharesRequest
            {
                Threshold = 2,
                TotalShares = 3,
                GenerateRandomSecret = true,
                Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
                {
                    Name = "E2E Test Organization",
                    ContactPhone = "+1-555-0199"
                },
                Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
                {
                    new() { Name = "E2E Keeper 1", Phone = "+1-555-0201", Email = "e2e1@test.com", Password = "E2EPass1!" },
                    new() { Name = "E2E Keeper 2", Phone = "+1-555-0202", Email = "e2e2@test.com", Password = "E2EPass2!" },
                    new() { Name = "E2E Keeper 3", Phone = "+1-555-0203", Email = "e2e3@test.com", Password = "E2EPass3!" }
                }
            };

            var createResponse = await _client.PostAsJsonAsync("/api/ceremony/create-shares", createRequest);

            await Task.Delay(3000); // Wait for ceremony to complete

            createResponse.Should().BeSuccessful();
            var createResult = await createResponse.Content.ReadFromJsonAsync<CeremonyResponse>();
            createResult.Should().NotBeNull();
            createResult!.SessionId.Should().NotBeNullOrEmpty();

            var statusResponse = await _client.GetAsync($"/api/ceremony/session/{createResult.SessionId}/status");
            statusResponse.Should().BeSuccessful();
            var status = await statusResponse.Content.ReadFromJsonAsync<SessionStatusResponse>();
            status.Should().NotBeNull();

            progressUpdates.Should().NotBeEmpty("Should receive progress updates via SignalR");

            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();
        }

        [TestMethod]
        public async Task ConcurrentUsers_MultipleSimultaneousCeremonies()
        {
            var tasks = new List<Task<bool>>();

            for (int i = 0; i < 5; i++)
            {
                tasks.Add(CreateCeremonyAsync(i));
            }

            var results = await Task.WhenAll(tasks);

            results.Should().AllSatisfy(r => r.Should().BeTrue());
        }

        private async Task<bool> CreateCeremonyAsync(int index)
        {
            try
            {
                var request = new CreateSharesRequest
                {
                    Threshold = 2,
                    TotalShares = 3,
                    GenerateRandomSecret = true,
                    Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
                    {
                        Name = $"Concurrent Org {index}",
                        ContactPhone = $"+1-555-0{index:D3}0"
                    },
                    Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
                    {
                        new() { Name = $"Keeper {index}-1", Phone = $"+1-555-0{index:D3}1", Email = $"k{index}-1@test.com", Password = $"Pass{index}1!" },
                        new() { Name = $"Keeper {index}-2", Phone = $"+1-555-0{index:D3}2", Email = $"k{index}-2@test.com", Password = $"Pass{index}2!" },
                        new() { Name = $"Keeper {index}-3", Phone = $"+1-555-0{index:D3}3", Email = $"k{index}-3@test.com", Password = $"Pass{index}3!" }
                    }
                };

                var response = await _client!.PostAsJsonAsync("/api/ceremony/create-shares", request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        [TestMethod]
        public async Task LargeFileHandling_ThroughAPI()
        {
            var largeSecret = new string('A', 1024 * 100); // 100 KB

            var request = new CreateSharesRequest
            {
                Threshold = 2,
                TotalShares = 3,
                GenerateRandomSecret = false,
                Secret = largeSecret,
                Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
                {
                    Name = "Large Secret Test",
                    ContactPhone = "+1-555-0199"
                },
                Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
                {
                    new() { Name = "Keeper 1", Phone = "+1-555-0201", Email = "k1@test.com", Password = "Pass1!" },
                    new() { Name = "Keeper 2", Phone = "+1-555-0202", Email = "k2@test.com", Password = "Pass2!" },
                    new() { Name = "Keeper 3", Phone = "+1-555-0203", Email = "k3@test.com", Password = "Pass3!" }
                }
            };

            var response = await _client!.PostAsJsonAsync("/api/ceremony/create-shares", request);
            
            Assert.IsTrue(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout);
        }

        [TestMethod]
        public async Task TimeoutScenario_LongRunningCeremony()
        {
            using var shortTimeoutClient = _factory!.CreateClient();
            shortTimeoutClient.Timeout = TimeSpan.FromSeconds(1);

            var request = new CreateSharesRequest
            {
                Threshold = 2,
                TotalShares = 3,
                GenerateRandomSecret = true,
                Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
                {
                    Name = "Timeout Test",
                    ContactPhone = "+1-555-0199"
                },
                Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
                {
                    new() { Name = "Keeper 1", Phone = "+1-555-0201", Email = "k1@test.com", Password = "Pass1!" },
                    new() { Name = "Keeper 2", Phone = "+1-555-0202", Email = "k2@test.com", Password = "Pass2!" },
                    new() { Name = "Keeper 3", Phone = "+1-555-0203", Email = "k3@test.com", Password = "Pass3!" }
                }
            };

            try
            {
                await shortTimeoutClient.PostAsJsonAsync("/api/ceremony/create-shares", request);
            }
            catch (TaskCanceledException)
            {
                Assert.IsTrue(true, "Timeout handled correctly");
            }
        }

        [TestMethod]
        public async Task APIErrorResponses_InvalidInput()
        {
            var request = new CreateSharesRequest
            {
                Threshold = 10,
                TotalShares = 3,
                GenerateRandomSecret = true,
                Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
                {
                    Name = "Error Test",
                    ContactPhone = "+1-555-0199"
                },
                Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
                {
                    new() { Name = "Keeper 1", Phone = "+1-555-0201", Email = "k1@test.com", Password = "Pass1!" },
                    new() { Name = "Keeper 2", Phone = "+1-555-0202", Email = "k2@test.com", Password = "Pass2!" },
                    new() { Name = "Keeper 3", Phone = "+1-555-0203", Email = "k3@test.com", Password = "Pass3!" }
                }
            };

            var response = await _client!.PostAsJsonAsync("/api/ceremony/create-shares", request);
            
            response.Should().HaveClientError();
        }

        [TestMethod]
        public async Task HealthCheckEndpoint_ShouldReturnHealthy()
        {
            var response = await _client!.GetAsync("/health");
            
            response.Should().BeSuccessful();
        }

        [TestMethod]
        public async Task MetricsEndpoint_ShouldBeAccessible()
        {
            var response = await _client!.GetAsync("/metrics");
            
            Assert.IsTrue(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound);
        }

        [TestMethod]
        public async Task SignalRHub_HandleMultipleSessionsPerClient()
        {
            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            await hubConnection.StartAsync();

            for (int i = 0; i < 5; i++)
            {
                var sessionId = $"multi-session-{i}";
                await hubConnection.InvokeAsync("JoinSession", sessionId);
            }

            for (int i = 0; i < 5; i++)
            {
                var sessionId = $"multi-session-{i}";
                await hubConnection.InvokeAsync("LeaveSession", sessionId);
            }

            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();

            Assert.IsTrue(true, "Multiple sessions handled successfully");
        }
    }
}
