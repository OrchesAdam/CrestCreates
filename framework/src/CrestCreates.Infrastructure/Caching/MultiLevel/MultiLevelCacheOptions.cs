using System;

namespace CrestCreates.Infrastructure.Caching.MultiLevel
{
    public class MultiLevelCacheOptions
    {
        public TimeSpan L1Expiration { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan L2Expiration { get; set; } = TimeSpan.FromMinutes(30);
        public bool EnableL1Sync { get; set; } = true;
        public string L1SyncChannel { get; set; } = "cache:sync:l1";
    }
}
