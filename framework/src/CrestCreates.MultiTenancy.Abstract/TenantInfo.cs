namespace CrestCreates.MultiTenancy.Abstract
{
    /// <summary>
    /// 租户信息默认实现
    /// </summary>
    public class TenantInfo : ITenantInfo
    {
        /// <summary>
        /// 租户唯一标识
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// 租户名称
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 租户专用数据库连接字符串
        /// </summary>
        public string? ConnectionString { get; set; }
        
        public TenantInfo()
        {
        }
        
        public TenantInfo(string id, string name, string? connectionString = null)
        {
            Id = id;
            Name = name;
            ConnectionString = connectionString;
        }
    }
}
