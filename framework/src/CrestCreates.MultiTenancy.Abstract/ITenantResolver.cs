using Microsoft.AspNetCore.Http;

namespace CrestCreates.MultiTenancy.Abstract
{
    /// <summary>
    /// 租户解析器接口
    /// 定义如何从 HTTP 请求中提取租户标识
    /// </summary>
    public interface ITenantResolver
    {
        /// <summary>
        /// 从 HTTP 上下文中解析租户ID
        /// </summary>
        /// <param name="httpContext">HTTP 上下文</param>
        /// <returns>租户ID,如果无法解析则返回 null</returns>
        Task<string> ResolveAsync(HttpContext httpContext);
    }
}
