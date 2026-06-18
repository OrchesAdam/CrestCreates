namespace CrestCreates.MultiTenancy.Abstract
{
    /// <summary>
    /// 租户提供者接口
    /// 定义如何获取租户信息
    /// </summary>
    public interface ITenantProvider
    {
        /// <summary>
        /// 根据租户ID获取租户信息
        /// </summary>
        /// <param name="tenantId">租户ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>租户信息,如果不存在则返回 null</returns>
        Task<ITenantInfo> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    }
}
