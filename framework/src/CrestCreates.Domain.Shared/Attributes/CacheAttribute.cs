using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class CacheAttribute : Attribute
    {
        public string KeyPrefix { get; }
        public int ExpirationMinutes { get; set; } = 10;
        public bool PerTenant { get; set; } = true;
        public bool IgnoreResult { get; set; } = false;

        public CacheAttribute(string keyPrefix)
        {
            KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        }
    }
}
