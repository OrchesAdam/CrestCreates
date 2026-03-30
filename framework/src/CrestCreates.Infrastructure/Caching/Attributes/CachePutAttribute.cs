using System;

namespace CrestCreates.Infrastructure.Caching.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class CachePutAttribute : Attribute
    {
        public string CacheName { get; }
        public string? Key { get; set; }
        public int Expiration { get; set; }
        public string? Condition { get; set; }
        public string? Unless { get; set; }

        public CachePutAttribute(string cacheName)
        {
            CacheName = cacheName ?? throw new ArgumentNullException(nameof(cacheName));
            Expiration = 300;
        }
    }
}
