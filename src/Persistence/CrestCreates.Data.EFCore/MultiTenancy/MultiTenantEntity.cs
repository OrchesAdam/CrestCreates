namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// 多租户实体基类
    /// </summary>
    public abstract class MultiTenantEntity : IMultiTenant
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public virtual string TenantId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 带主键的多租户实体基类
    /// </summary>
    public abstract class MultiTenantEntity<TKey> : MultiTenantEntity
    {
        public virtual TKey Id { get; set; }
    }
}
