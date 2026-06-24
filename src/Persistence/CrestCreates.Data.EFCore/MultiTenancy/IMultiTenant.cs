namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// 多租户实体接口
    /// 实现此接口的实体会自动应用租户过滤器
    /// </summary>
    public interface IMultiTenant
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        string TenantId { get; set; }
    }
}
