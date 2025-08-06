using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Shamir.Ceremony.Common.Configuration;

namespace Shamir.Ceremony.Common.Storage;

public class MongoDbKeyValueStore : IKeyValueStore
{
    private readonly IMongoCollection<KeyValueDocument> _collection;
    private readonly JsonSerializerOptions _jsonOptions;

    public MongoDbKeyValueStore(IOptions<MongoDbSettings> options)
    {
        var settings = options.Value;
        var client = new MongoClient(settings.ConnectionString);
        var database = client.GetDatabase(settings.DatabaseName);
        _collection = database.GetCollection<KeyValueDocument>(settings.CollectionName);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        CreateIndexes();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var filter = Builders<KeyValueDocument>.Filter.Eq(x => x.Key, key);
        var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

        if (document == null || (document.ExpiresAt.HasValue && document.ExpiresAt.Value < DateTime.UtcNow))
        {
            if (document != null && document.ExpiresAt.HasValue && document.ExpiresAt.Value < DateTime.UtcNow)
            {
                await DeleteAsync(key, cancellationToken);
            }
            return null;
        }

        return JsonSerializer.Deserialize<T>(document.Value, _jsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var filter = Builders<KeyValueDocument>.Filter.Eq(x => x.Key, key);
        
        var existingDocument = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        
        if (existingDocument != null)
        {
            var update = Builders<KeyValueDocument>.Update
                .Set(x => x.Value, json)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.ExpiresAt, expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null);
            
            await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        }
        else
        {
            var document = new KeyValueDocument
            {
                Key = key,
                Value = json,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null
            };
            
            await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var filter = Builders<KeyValueDocument>.Filter.Eq(x => x.Key, key);
        var result = await _collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount > 0;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var filter = Builders<KeyValueDocument>.Filter.Eq(x => x.Key, key);
        var count = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }

    public async Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default)
    {
        FilterDefinition<KeyValueDocument> filter;
        
        if (pattern == "*")
        {
            filter = Builders<KeyValueDocument>.Filter.Empty;
        }
        else
        {
            var regexPattern = pattern.Replace("*", ".*");
            filter = Builders<KeyValueDocument>.Filter.Regex(x => x.Key, new BsonRegularExpression(regexPattern));
        }

        var projection = Builders<KeyValueDocument>.Projection.Include(x => x.Key);
        var documents = await _collection.Find(filter).Project(projection).ToListAsync(cancellationToken);
        
        return documents.Select(doc => doc["Key"].AsString);
    }

    private void CreateIndexes()
    {
        var keyIndex = Builders<KeyValueDocument>.IndexKeys.Ascending(x => x.Key);
        var expiryIndex = Builders<KeyValueDocument>.IndexKeys.Ascending(x => x.ExpiresAt);
        
        _collection.Indexes.CreateOne(new CreateIndexModel<KeyValueDocument>(keyIndex, new CreateIndexOptions { Unique = true }));
        _collection.Indexes.CreateOne(new CreateIndexModel<KeyValueDocument>(expiryIndex, new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
    }
}

public class KeyValueDocument
{
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
