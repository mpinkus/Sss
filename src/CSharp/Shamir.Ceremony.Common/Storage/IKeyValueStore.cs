using System.Text.Json;

namespace Shamir.Ceremony.Common.Storage;

public interface IKeyValueStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default);
}
