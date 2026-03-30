using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using System.Text.Json;

namespace CrestCreates.Infrastructure.Caching
{
    public class RedisCache : ICache
    {
        private readonly IDatabase _database;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public RedisCache(IConnectionMultiplexer connectionMultiplexer)
        {
            _database = connectionMultiplexer.GetDatabase();
            _jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                IncludeFields = true
            };
        }

        public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await _database.StringGetAsync(key);
            if (value.HasValue)
            {
                string jsonValue = value.ToString();
                return JsonSerializer.Deserialize<T>(jsonValue, _jsonSerializerOptions);
            }
            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var jsonValue = JsonSerializer.Serialize(value, _jsonSerializerOptions);
            if (expiration.HasValue)
            {
                await _database.StringSetAsync(key, jsonValue, expiration.Value);
            }
            else
            {
                await _database.StringSetAsync(key, jsonValue);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _database.KeyDeleteAsync(key);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _database.KeyExistsAsync(key);
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoints = _database.Multiplexer.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = _database.Multiplexer.GetServer(endpoint);
                await server.FlushDatabaseAsync(_database.Database);
            }
        }
    }
}