using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Storage;
using Testcontainers.MongoDb;

namespace Shamir.Ceremony.Web.Api.Tests.Integration
{
    [TestClass]
    public class MongoDbAdvancedIntegrationTests
    {
        private static MongoDbContainer? _mongoContainer;
        private static WebApplicationFactory<Program>? _factory;
        private IKeyValueStore? _keyValueStore;

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
        }

        [TestInitialize]
        public void TestInitialize()
        {
            using var scope = _factory!.Services.CreateScope();
            _keyValueStore = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();
        }

        [ClassCleanup]
        public static async Task ClassCleanup()
        {
            _factory?.Dispose();

            if (_mongoContainer != null)
            {
                await _mongoContainer.DisposeAsync();
            }
        }

        [TestMethod]
        public async Task KeyValueStore_LargeDocument_ShouldPersist()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var largeData = new
            {
                Id = Guid.NewGuid().ToString(),
                Data = new string('A', 1024 * 1024), // 1 MB of data
                Timestamp = DateTime.UtcNow
            };

            var key = "large-doc-" + Guid.NewGuid();

            await store.SetAsync(key, largeData);
            var retrieved = await store.GetAsync<object>(key);

            retrieved.Should().NotBeNull();
            await store.DeleteAsync(key);
        }

        [TestMethod]
        public async Task KeyValueStore_ConcurrentWrites_ShouldSucceed()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var keys = Enumerable.Range(0, 10).Select(i => $"concurrent-{i}-{Guid.NewGuid()}").ToList();
            var tasks = keys.Select(async (key, index) =>
            {
                var data = new { Index = index, Value = $"data-{index}", Timestamp = DateTime.UtcNow };
                await store.SetAsync(key, data);
            });

            await Task.WhenAll(tasks);

            foreach (var key in keys)
            {
                var exists = await store.ExistsAsync(key);
                exists.Should().BeTrue($"Key {key} should exist");
                await store.DeleteAsync(key);
            }
        }

        [TestMethod]
        public async Task KeyValueStore_ConcurrentReads_ShouldSucceed()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var key = "concurrent-read-" + Guid.NewGuid();
            var data = new { Message = "Test data", Timestamp = DateTime.UtcNow };

            await store.SetAsync(key, data);

            var readTasks = Enumerable.Range(0, 10).Select(async _ =>
            {
                return await store.GetAsync<object>(key);
            });

            var results = await Task.WhenAll(readTasks);

            results.Should().AllSatisfy(r => r.Should().NotBeNull());
            await store.DeleteAsync(key);
        }

        [TestMethod]
        public async Task KeyValueStore_ExpiryWithMultipleKeys_ShouldExpireCorrectly()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var keys = new[]
            {
                ("short-expiry-" + Guid.NewGuid(), TimeSpan.FromSeconds(1)),
                ("medium-expiry-" + Guid.NewGuid(), TimeSpan.FromSeconds(3)),
                ("long-expiry-" + Guid.NewGuid(), TimeSpan.FromSeconds(5))
            };

            foreach (var (key, expiry) in keys)
            {
                await store.SetAsync(key, new { Data = "test" }, expiry);
            }

            await Task.Delay(TimeSpan.FromSeconds(1.5));

            var shortExists = await store.ExistsAsync(keys[0].Item1);
            var mediumExists = await store.ExistsAsync(keys[1].Item1);
            var longExists = await store.ExistsAsync(keys[2].Item1);

            shortExists.Should().BeFalse("Short expiry should have expired");
            mediumExists.Should().BeTrue("Medium expiry should still exist");
            longExists.Should().BeTrue("Long expiry should still exist");

            await Task.Delay(TimeSpan.FromSeconds(2));

            mediumExists = await store.ExistsAsync(keys[1].Item1);
            longExists = await store.ExistsAsync(keys[2].Item1);

            mediumExists.Should().BeFalse("Medium expiry should have expired");
            longExists.Should().BeTrue("Long expiry should still exist");
        }

        [TestMethod]
        public async Task KeyValueStore_UpdateExistingKey_ShouldOverwrite()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var key = "update-test-" + Guid.NewGuid();
            var initialData = new { Version = 1, Message = "Initial" };
            var updatedData = new { Version = 2, Message = "Updated" };

            await store.SetAsync(key, initialData);
            var retrieved1 = await store.GetAsync<dynamic>(key);
            ((int)retrieved1!.Version).Should().Be(1);

            await store.SetAsync(key, updatedData);
            var retrieved2 = await store.GetAsync<dynamic>(key);
            ((int)retrieved2!.Version).Should().Be(2);

            await store.DeleteAsync(key);
        }

        [TestMethod]
        public async Task KeyValueStore_DeleteNonExistentKey_ShouldReturnFalse()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var key = "non-existent-" + Guid.NewGuid();
            var deleted = await store.DeleteAsync(key);

            deleted.Should().BeFalse("Deleting non-existent key should return false");
        }

        [TestMethod]
        public async Task KeyValueStore_MultipleDeleteOperations_ShouldHandleCorrectly()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var key = "delete-test-" + Guid.NewGuid();
            await store.SetAsync(key, new { Data = "test" });

            var deleted1 = await store.DeleteAsync(key);
            deleted1.Should().BeTrue("First delete should succeed");

            var deleted2 = await store.DeleteAsync(key);
            deleted2.Should().BeFalse("Second delete should return false");
        }

        [TestMethod]
        public async Task KeyValueStore_ComplexNestedObject_ShouldSerializeCorrectly()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var complexData = new
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Nested = new
                {
                    Level1 = "value1",
                    Level2 = new
                    {
                        Level3 = "value3",
                        Array = new[] { 1, 2, 3, 4, 5 }
                    }
                },
                List = new List<string> { "item1", "item2", "item3" },
                Dictionary = new Dictionary<string, object>
                {
                    ["key1"] = "value1",
                    ["key2"] = 123,
                    ["key3"] = true
                }
            };

            var key = "complex-" + Guid.NewGuid();

            await store.SetAsync(key, complexData);
            var retrieved = await store.GetAsync<object>(key);

            retrieved.Should().NotBeNull();
            await store.DeleteAsync(key);
        }

        [TestMethod]
        public async Task KeyValueStore_SessionStateLifecycle_ShouldWork()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var sessionId = Guid.NewGuid().ToString();
            var key = $"session:{sessionId}";

            var initialState = new
            {
                Status = "INITIALIZING",
                ProgressPercentage = 0,
                CurrentStep = "Starting",
                Events = new List<string> { "Session started" },
                LastUpdated = DateTime.UtcNow
            };

            await store.SetAsync(key, initialState, TimeSpan.FromHours(1));
            var exists1 = await store.ExistsAsync(key);
            exists1.Should().BeTrue();

            var updatedState = new
            {
                Status = "PROCESSING",
                ProgressPercentage = 50,
                CurrentStep = "Generating shares",
                Events = new List<string> { "Session started", "Generating shares" },
                LastUpdated = DateTime.UtcNow
            };

            await store.SetAsync(key, updatedState, TimeSpan.FromHours(1));
            var retrieved = await store.GetAsync<dynamic>(key);
            ((string)retrieved!.Status).Should().Be("PROCESSING");

            await store.DeleteAsync(key);
            var exists2 = await store.ExistsAsync(key);
            exists2.Should().BeFalse();
        }

        [TestMethod]
        public async Task KeyValueStore_HighVolumeOperations_ShouldMaintainPerformance()
        {
            using var scope = _factory!.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IKeyValueStore>();

            var count = 100;
            var keys = new List<string>();

            var writeTasks = Enumerable.Range(0, count).Select(async i =>
            {
                var key = $"perf-test-{i}-{Guid.NewGuid()}";
                keys.Add(key);
                await store.SetAsync(key, new { Index = i, Data = $"data-{i}" });
            });

            await Task.WhenAll(writeTasks);

            var readTasks = keys.Select(async key =>
            {
                return await store.GetAsync<object>(key);
            });

            var results = await Task.WhenAll(readTasks);
            results.Should().AllSatisfy(r => r.Should().NotBeNull());

            var deleteTasks = keys.Select(key => store.DeleteAsync(key));
            await Task.WhenAll(deleteTasks);
        }
    }
}
