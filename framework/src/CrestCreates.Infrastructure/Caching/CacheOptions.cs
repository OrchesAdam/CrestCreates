using System;

namespace CrestCreates.Infrastructure.Caching
{
    public class CacheOptions
    {
        public string Provider { get; set; } = "memory";
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);
        public string RedisConnectionString { get; set; } = "localhost:6379";
        public int RedisDatabase { get; set; } = 0;
        public bool EnableKeyPrefix { get; set; } = true;
        public string KeyPrefix { get; set; } = "crestcreates:";
    }
}