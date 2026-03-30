using System;

namespace CrestCreates.MultiTenancy
{
    /// <summary>
    /// 多租户配置选项
    /// </summary>
    public class MultiTenancyOptions
    {
        /// <summary>
        /// 租户识别策略
        /// </summary>
        public TenantResolutionStrategy ResolutionStrategy { get; set; } = TenantResolutionStrategy.Header;

        /// <summary>
        /// 租户隔离策略
        /// </summary>
        public TenantIsolationStrategy IsolationStrategy { get; set; } = TenantIsolationStrategy.Database;

        /// <summary>
        /// HTTP Header 名称（用于 Header 策略）
        /// </summary>
        public string TenantHeaderName { get; set; } = "X-Tenant-Id";

        /// <summary>
        /// 查询字符串参数名（用于 QueryString 策略）
        /// </summary>
        public string TenantQueryStringKey { get; set; } = "tenantId";

        /// <summary>
        /// Cookie 名称（用于 Cookie 策略）
        /// </summary>
        public string TenantCookieName { get; set; } = "TenantId";

        /// <summary>
        /// 路由参数名（用于 Route 策略）
        /// </summary>
        public string TenantRouteKey { get; set; } = "tenantId";

        /// <summary>
        /// 根域名（用于 Subdomain 策略）
        /// 例如: example.com，则 tenant1.example.com 会解析为 tenant1
        /// </summary>
        public string RootDomain { get; set; }

        /// <summary>
        /// 默认租户ID（当无法解析租户时使用）
        /// </summary>
        public string DefaultTenantId { get; set; }

        /// <summary>
        /// 是否允许无租户访问
        /// </summary>
        public bool AllowNoTenant { get; set; } = false;

        /// <summary>
        /// 租户ID字段名（用于鉴别器模式）
        /// </summary>
        public string TenantIdColumnName { get; set; } = "TenantId";

        /// <summary>
        /// 是否启用租户缓存
        /// </summary>
        public bool EnableTenantCache { get; set; } = true;

        /// <summary>
        /// 租户缓存过期时间（分钟）
        /// </summary>
        public int TenantCacheExpirationMinutes { get; set; } = 60;
    }

    /// <summary>
    /// 租户识别策略
    /// </summary>
    [Flags]
    public enum TenantResolutionStrategy
    {
        /// <summary>
        /// 从 HTTP Header 中解析
        /// </summary>
        Header = 1,

        /// <summary>
        /// 从子域名中解析
        /// </summary>
        Subdomain = 2,

        /// <summary>
        /// 从查询字符串中解析
        /// </summary>
        QueryString = 4,

        /// <summary>
        /// 从 Cookie 中解析
        /// </summary>
        Cookie = 8,

        /// <summary>
        /// 从路由参数中解析
        /// </summary>
        Route = 16,

        /// <summary>
        /// 组合策略：Header + Subdomain（推荐）
        /// </summary>
        HeaderOrSubdomain = Header | Subdomain,

        /// <summary>
        /// 组合策略：所有方式
        /// </summary>
        All = Header | Subdomain | QueryString | Cookie | Route
    }

    /// <summary>
    /// 租户隔离策略
    /// </summary>
    public enum TenantIsolationStrategy
    {
        /// <summary>
        /// 每个租户独立数据库
        /// </summary>
        Database,

        /// <summary>
        /// 共享数据库，使用鉴别器字段（TenantId）隔离
        /// </summary>
        Discriminator,

        /// <summary>
        /// 每个租户独立 Schema
        /// </summary>
        Schema
    }
}
