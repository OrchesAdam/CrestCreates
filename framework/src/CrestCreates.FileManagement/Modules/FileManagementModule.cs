using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.FileManagement.Services;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Configuration;

namespace CrestCreates.FileManagement.Modules;

/// <summary>
/// 文件管理模块
/// </summary>
public class FileManagementModule : ModuleBase
{
    private readonly FileManagementOptions _options;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="configuration">配置对象</param>
    public FileManagementModule(IConfiguration configuration)
    {
        // 从配置系统读取文件管理配置
        _options = new FileManagementOptions();
        configuration.GetSection("FileManagement").Bind(_options);
    }
    
    /// <summary>
    /// 配置服务
    /// </summary>
    /// <param name="services">服务集合</param>
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);
        
        // 注册配置
        services.AddSingleton(_options);
        services.AddSingleton(_options.LocalFileSystem);
        services.AddSingleton(_options.Validation);
        services.AddSingleton(_options.Url);
        
        // 注册存储提供者
        switch (_options.ProviderType)
        {
            case StorageProviderType.LocalFileSystem:
                services.AddSingleton<IFileStorageProvider, LocalFileSystemProvider>();
                break;
            // 可以添加其他存储提供者的注册
            // case StorageProviderType.AzureBlobStorage:
            //     services.AddSingleton<IFileStorageProvider, AzureBlobStorageProvider>();
            //     break;
            // case StorageProviderType.AmazonS3:
            //     services.AddSingleton<IFileStorageProvider, AmazonS3Provider>();
            //     break;
        }
        
        // 注册文件管理服务
        services.AddSingleton<IFileManagementService, FileManagementService>();
    }
}