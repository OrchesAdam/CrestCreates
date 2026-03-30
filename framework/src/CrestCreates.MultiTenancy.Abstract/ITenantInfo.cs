namespace CrestCreates.MultiTenancy.Abstract
{
    /// <summary>
    /// 租户信息接口
    /// 定义租户的基本信息
    /// </summary>
    public interface ITenantInfo
    {
        /// <summary>
        /// 租户唯一标识
        /// </summary>
        string Id { get; }
        
        /// <summary>
        /// 租户名称
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 租户专用数据库连接字符串
        /// </summary>
        string? ConnectionString { get; }
    }
}
