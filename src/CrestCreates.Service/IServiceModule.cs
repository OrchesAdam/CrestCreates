using System.Threading.Tasks;
using CrestCreates.Data;
using CrestCreates.Modularity;

namespace CrestCreates.Service;

/// <summary>
/// 服务模块配置选项
/// </summary>
public class ServiceModuleOptions
{
    /// <summary>
    /// API基础地址
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCaching { get; set; } = true;
    
    /// <summary>
    /// 缓存过期时间（分钟）
    /// </summary>
    public int CacheExpirationMinutes { get; set; } = 30;
}

/// <summary>
/// 服务模块接口，依赖于数据模块
/// </summary>
[ModuleInterface(ConfigurationType = typeof(ServiceModuleOptions))]
[DependsOn(typeof(DataModule))] // 这里使用DependsOn特性指定依赖
public interface IServiceModule : ICrestCreatesModule
{
    /// <summary>
    /// 初始化服务
    /// </summary>
    Task InitializeServicesAsync();
    
    /// <summary>
    /// 获取API基础地址
    /// </summary>
    string GetApiBaseUrl();
    
    /// <summary>
    /// 清除缓存
    /// </summary>
    Task ClearCacheAsync();
    
    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <returns>健康状态</returns>
    Task<bool> HealthCheckAsync();
}
