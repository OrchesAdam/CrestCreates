using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CrestCreates.Infrastructure.Caching
{
    public class RedisCache : ICache
    {
        private readonly ConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;

        public RedisCache(string connectionString)
        {
            _connectionMultiplexer = ConnectionMultiplexer.Connect(connectionString);
            _database = _connectionMultiplexer.GetDatabase();
        }

        public RedisCache(ConnectionMultiplexer connectionMultiplexer)
        {
            _connectionMultiplexer = connectionMultiplexer;
            _database = connectionMultiplexer.GetDatabase();
        }

        public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var value = await _database.StringGetAsync(key);
                if (value.HasValue)
                {
                    return JsonSerializer.Deserialize<T>(value.ToString());
                }
                return default;
            }
            catch (RedisConnectionException ex)
            {
                // 处理 Redis 连接失败，返回默认值
                Console.WriteLine($"Redis connection error: {ex.Message}");
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var jsonValue = JsonSerializer.Serialize(value);
                if (expiration.HasValue)
                {
                    await _database.StringSetAsync(key, jsonValue, expiration.Value);
                }
                else
                {
                    await _database.StringSetAsync(key, jsonValue);
                }
            }
            catch (RedisConnectionException ex)
            {
                // 处理 Redis 连接失败，记录错误但不影响主流程
                Console.WriteLine($"Redis connection error: {ex.Message}");
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                await _database.KeyDeleteAsync(key);
            }
            catch (RedisConnectionException ex)
            {
                // 处理 Redis 连接失败，记录错误但不影响主流程
                Console.WriteLine($"Redis connection error: {ex.Message}");
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                return await _database.KeyExistsAsync(key);
            }
            catch (RedisConnectionException ex)
            {
                // 处理 Redis 连接失败，返回 false
                Console.WriteLine($"Redis connection error: {ex.Message}");
                return false;
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints()[0]);
                await server.FlushDatabaseAsync(_database.Database);
            }
            catch (RedisConnectionException ex)
            {
                // 处理 Redis 连接失败，记录错误但不影响主流程
                Console.WriteLine($"Redis connection error: {ex.Message}");
            }
        }

        // 健康检查方法
        public bool IsHealthy()
        {
            try
            {
                return _connectionMultiplexer.IsConnected;
            }
            catch
            {
                return false;
            }
        }
    }
}