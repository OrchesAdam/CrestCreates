using System.Collections.Generic;
using CrestCreates.PluginSystem.Models;

namespace CrestCreates.PluginSystem.Services;

/// <summary>
/// 插件管理器接口
/// 注意：插件系统依赖运行时反射和动态程序集加载，与 NativeAOT 不兼容。
/// 使用时需确保发布配置排除此模块或使用完整框架发布。
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// 发现插件目录中的所有插件
    /// </summary>
    IReadOnlyList<PluginInfo> DiscoverPlugins(string pluginDirectory);

    /// <summary>
    /// 加载插件
    /// </summary>
    PluginLoadResult LoadPlugin(PluginInfo pluginInfo);

    /// <summary>
    /// 初始化已加载的插件
    /// </summary>
    void InitializePlugin(PluginInfo pluginInfo, IServiceProvider serviceProvider);

    /// <summary>
    /// 获取所有已加载的插件
    /// </summary>
    IReadOnlyList<PluginInfo> GetLoadedPlugins();

    /// <summary>
    /// 禁用插件
    /// </summary>
    void DisablePlugin(string pluginId);

    /// <summary>
    /// 启用插件
    /// </summary>
    void EnablePlugin(string pluginId);

    /// <summary>
    /// 验证插件依赖
    /// </summary>
    bool ValidateDependencies(PluginManifest manifest, IEnumerable<PluginInfo> loadedPlugins);
}
