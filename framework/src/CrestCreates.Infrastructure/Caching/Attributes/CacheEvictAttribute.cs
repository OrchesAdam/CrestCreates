using System;

namespace CrestCreates.Infrastructure.Caching.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class CacheEvictAttribute : Attribute
    {
        public string CacheName { get; }
        public string? Key { get; set; }
        public bool AllEntries { get; set; }
        public bool BeforeInvocation { get; set; }
        public string? Condition { get; set; }

        public CacheEvictAttribute(string cacheName)
        {
            CacheName = cacheName ?? throw new ArgumentNullException(nameof(cacheName));
        }
    }
}
