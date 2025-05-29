using System;
using System.Threading.Tasks;
using CrestCreates.Modularity;

namespace CrestCreates.Data;

/// <summary>
/// 数据模块接口
/// </summary>
[ModuleInterface(ConfigurationType = typeof(DataModuleOptions))]
public interface IDataModule : ICrestCreatesModule
{
    /// <summary>
    /// 初始化数据库
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// 执行数据库迁移
    /// </summary>
    void MigrateDatabase();
    
    /// <summary>
    /// 获取连接字符串
    /// </summary>
    string GetConnectionString();
    
    /// <summary>
    /// 添加种子数据
    /// </summary>
    /// <param name="clearExisting">是否清除现有数据</param>
    Task SeedDataAsync(bool clearExisting = false);
}
