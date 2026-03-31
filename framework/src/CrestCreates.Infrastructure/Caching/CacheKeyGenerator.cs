using System;
using System.Collections.Generic;
using System.Text;

namespace CrestCreates.Infrastructure.Caching
{
    public static class CacheKeyGenerator
    {
        private const string DefaultVersion = "v1";

        /// <summary>
        /// 生成缓存键
        /// </summary>
        /// <param name="prefix">前缀</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateKey(string prefix, params object[] parts)
        {
            return GenerateKeyWithVersion(DefaultVersion, prefix, parts);
        }

        /// <summary>
        /// 生成带版本的缓存键
        /// </summary>
        /// <param name="version">版本号</param>
        /// <param name="prefix">前缀</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateKeyWithVersion(string version, string prefix, params object[] parts)
        {
            var sb = new StringBuilder();
            sb.Append(version);
            sb.Append(":");
            sb.Append(prefix);
            foreach (var part in parts)
            {
                sb.Append(":");
                sb.Append(part?.ToString() ?? "null");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 生成带命名空间的缓存键
        /// </summary>
        /// <param name="namespace">命名空间</param>
        /// <param name="prefix">前缀</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateKeyWithNamespace(string @namespace, string prefix, params object[] parts)
        {
            var sb = new StringBuilder();
            sb.Append(DefaultVersion);
            sb.Append(":");
            sb.Append(@namespace);
            sb.Append(":");
            sb.Append(prefix);
            foreach (var part in parts)
            {
                sb.Append(":");
                sb.Append(part?.ToString() ?? "null");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 生成带版本和命名空间的缓存键
        /// </summary>
        /// <param name="version">版本号</param>
        /// <param name="namespace">命名空间</param>
        /// <param name="prefix">前缀</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateKeyWithVersionAndNamespace(string version, string @namespace, string prefix, params object[] parts)
        {
            var sb = new StringBuilder();
            sb.Append(version);
            sb.Append(":");
            sb.Append(@namespace);
            sb.Append(":");
            sb.Append(prefix);
            foreach (var part in parts)
            {
                sb.Append(":");
                sb.Append(part?.ToString() ?? "null");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 生成用户相关的缓存键
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateUserKey(int userId, params object[] parts)
        {
            var allParts = new List<object> { userId };
            allParts.AddRange(parts);
            return GenerateKey("user", allParts.ToArray());
        }

        /// <summary>
        /// 生成产品相关的缓存键
        /// </summary>
        /// <param name="productId">产品ID</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateProductKey(int productId, params object[] parts)
        {
            var allParts = new List<object> { productId };
            allParts.AddRange(parts);
            return GenerateKey("product", allParts.ToArray());
        }

        /// <summary>
        /// 生成订单相关的缓存键
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateOrderKey(int orderId, params object[] parts)
        {
            var allParts = new List<object> { orderId };
            allParts.AddRange(parts);
            return GenerateKey("order", allParts.ToArray());
        }

        /// <summary>
        /// 生成实体相关的缓存键
        /// </summary>
        /// <param name="entityName">实体名称</param>
        /// <param name="id">实体ID</param>
        /// <param name="parts">键的组成部分</param>
        /// <returns>生成的缓存键</returns>
        public static string GenerateCacheKey(string entityName, object id, params object[] parts)
        {
            var allParts = new List<object> { id };
            allParts.AddRange(parts);
            return GenerateKey(entityName, allParts.ToArray());
        }
    }
}