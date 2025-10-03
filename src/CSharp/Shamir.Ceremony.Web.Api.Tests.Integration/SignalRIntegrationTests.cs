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
    public class SignalRIntegrationTests
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
        public async Task SignalRHub_Connection_ShouldSucceed()
        {
            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            await hubConnection.StartAsync();
            Assert.AreEqual(HubConnectionState.Connected, hubConnection.State);

            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();
        }

        [TestMethod]
        public async Task SignalRHub_JoinSession_ShouldReceiveMessages()
        {
            var sessionId = $"test-session-{Guid.NewGuid():N}";
            var receivedMessages = new List<string>();

            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            hubConnection.On<object>("ProgressUpdate", (message) =>
            {
                receivedMessages.Add("ProgressUpdate");
            });

            await hubConnection.StartAsync();
            await hubConnection.InvokeAsync("JoinSession", sessionId);

            await Task.Delay(500);

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

            await Task.Delay(2000);

            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();

            Assert.IsTrue(receivedMessages.Count > 0, "Should receive progress updates via SignalR");
        }

        [TestMethod]
        public async Task SignalRHub_ProvideInput_ShouldStoreInKeyValueStore()
        {
            var sessionId = $"test-session-{Guid.NewGuid():N}";
            var inputType = "TEST_INPUT";
            var inputValue = "test-value";

            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            await hubConnection.StartAsync();
            await hubConnection.InvokeAsync("ProvideInput", sessionId, inputType, inputValue);
            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();

            using var scope = _factory!.Services.CreateScope();
            var keyValueStore = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();
            var key = $"input:{sessionId}:{inputType.ToLower()}";
            
            var stored = await keyValueStore.GetAsync<object>(key);
            Assert.IsNotNull(stored);
        }

        [TestMethod]
        public async Task SignalRHub_MultipleClients_ShouldReceiveMessages()
        {
            var sessionId = $"test-session-{Guid.NewGuid():N}";
            var client1Messages = new List<string>();
            var client2Messages = new List<string>();

            var hubConnection1 = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            var hubConnection2 = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            hubConnection1.On<object>("ProgressUpdate", (message) =>
            {
                client1Messages.Add("ProgressUpdate");
            });

            hubConnection2.On<object>("ProgressUpdate", (message) =>
            {
                client2Messages.Add("ProgressUpdate");
            });

            await hubConnection1.StartAsync();
            await hubConnection2.StartAsync();

            await hubConnection1.InvokeAsync("JoinSession", sessionId);
            await hubConnection2.InvokeAsync("JoinSession", sessionId);

            await Task.Delay(500);

            var request = new CreateSharesRequest
            {
                Threshold = 2,
                TotalShares = 2,
                GenerateRandomSecret = true,
                Organization = new Shamir.Ceremony.Common.Models.OrganizationInfo
                {
                    Name = "Test Organization",
                    ContactPhone = "+1-555-0123"
                },
                Keepers = new List<Shamir.Ceremony.Web.Api.Models.KeeperInfo>
                {
                    new() { Name = "Keeper 1", Phone = "+1-555-0101", Email = "keeper1@test.com", Password = "SecurePass1!" },
                    new() { Name = "Keeper 2", Phone = "+1-555-0102", Email = "keeper2@test.com", Password = "SecurePass2!" }
                }
            };

            await _client!.PostAsJsonAsync("/api/ceremony/create-shares", request);
            await Task.Delay(2000);

            await hubConnection1.StopAsync();
            await hubConnection2.StopAsync();
            await hubConnection1.DisposeAsync();
            await hubConnection2.DisposeAsync();

            Assert.IsTrue(client1Messages.Count > 0, "Client 1 should receive messages");
            Assert.IsTrue(client2Messages.Count > 0, "Client 2 should receive messages");
        }

        [TestMethod]
        public async Task SignalRHub_LeaveSession_ShouldNotReceiveMoreMessages()
        {
            var sessionId = $"test-session-{Guid.NewGuid():N}";
            var receivedMessages = new List<string>();

            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .Build();

            hubConnection.On<object>("ProgressUpdate", (message) =>
            {
                receivedMessages.Add("ProgressUpdate");
            });

            await hubConnection.StartAsync();
            await hubConnection.InvokeAsync("JoinSession", sessionId);
            
            await Task.Delay(500);
            receivedMessages.Clear();

            await hubConnection.InvokeAsync("LeaveSession", sessionId);
            await Task.Delay(200);

            var messageCountAfterLeave = receivedMessages.Count;

            await Task.Delay(500);

            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();

            Assert.AreEqual(0, receivedMessages.Count, "Should not receive messages after leaving session");
        }

        [TestMethod]
        public async Task SignalRHub_ReconnectionAfterDisconnect_ShouldSucceed()
        {
            var hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_client!.BaseAddress}ceremonyhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory!.Server.CreateHandler();
                })
                .WithAutomaticReconnect()
                .Build();

            await hubConnection.StartAsync();
            Assert.AreEqual(HubConnectionState.Connected, hubConnection.State);

            await hubConnection.StopAsync();
            Assert.AreEqual(HubConnectionState.Disconnected, hubConnection.State);

            await hubConnection.StartAsync();
            Assert.AreEqual(HubConnectionState.Connected, hubConnection.State);

            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();
        }
    }
}
