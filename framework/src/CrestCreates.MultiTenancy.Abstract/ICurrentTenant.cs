namespace CrestCreates.MultiTenancy.Abstract
{
    /// <summary>
    /// 当前租户上下文接口
    /// 提供访问当前租户信息和切换租户上下文的功能
    /// </summary>
    public interface ICurrentTenant
    {
        /// <summary>
        /// 当前租户信息
        /// </summary>
        ITenantInfo Tenant { get; }
        
        /// <summary>
        /// 当前租户ID
        /// </summary>
        string Id { get; }
        
        /// <summary>
        /// 临时切换租户上下文
        /// </summary>
        /// <param name="tenantId">要切换到的租户ID</param>
        /// <returns>用于恢复原租户上下文的 IDisposable 对象</returns>
        IDisposable Change(string tenantId);
    }
}
