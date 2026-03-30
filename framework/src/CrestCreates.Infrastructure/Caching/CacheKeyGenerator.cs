using System;
using System.Collections.Generic;
using System.Text;

namespace CrestCreates.Infrastructure.Caching
{
    public static class CacheKeyGenerator
    {
        public static string GenerateKey(string prefix, params object[] parts)
        {
            var sb = new StringBuilder();
            sb.Append(prefix);
            foreach (var part in parts)
            {
                sb.Append(":");
                sb.Append(part?.ToString() ?? "null");
            }
            return sb.ToString();
        }

        public static string GenerateUserKey(int userId, params object[] parts)
        {
            var allParts = new List<object> { userId };
            allParts.AddRange(parts);
            return GenerateKey("user", allParts.ToArray());
        }

        public static string GenerateProductKey(int productId, params object[] parts)
        {
            var allParts = new List<object> { productId };
            allParts.AddRange(parts);
            return GenerateKey("product", allParts.ToArray());
        }

        public static string GenerateOrderKey(int orderId, params object[] parts)
        {
            var allParts = new List<object> { orderId };
            allParts.AddRange(parts);
            return GenerateKey("order", allParts.ToArray());
        }

        public static string GenerateCacheKey(string entityName, object id, params object[] parts)
        {
            var allParts = new List<object> { id };
            allParts.AddRange(parts);
            return GenerateKey(entityName, allParts.ToArray());
        }
    }
}