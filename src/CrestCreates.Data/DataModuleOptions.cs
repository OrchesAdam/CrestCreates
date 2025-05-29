namespace CrestCreates.Data;

/// <summary>
/// 数据模块配置选项
/// </summary>
public class DataModuleOptions
{
    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否自动执行迁移
    /// </summary>
    public bool AutoMigrate { get; set; } = false;
    
    /// <summary>
    /// 数据库类型
    /// </summary>
    public string DatabaseType { get; set; } = "SqlServer";
    
    /// <summary>
    /// 命令超时时间（秒）
    /// </summary>
    public int CommandTimeout { get; set; } = 30;
    
    /// <summary>
    /// 是否启用连接池
    /// </summary>
    public bool EnableConnectionPooling { get; set; } = true;
}