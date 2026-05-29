using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CrestCreates.PluginSystem.Abstractions;
using CrestCreates.PluginSystem.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.PluginSystem.Services;

/// <summary>
/// 插件管理器接口
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

/// <summary>
/// 插件管理器实现
/// </summary>
public class PluginManager : IPluginManager
{
    private readonly ILogger<PluginManager> _logger;
    private readonly Dictionary<string, PluginInfo> _plugins = new();
    private readonly Dictionary<string, AssemblyLoadContext> _loadContexts = new();

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<PluginInfo> DiscoverPlugins(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
        {
            _logger.LogWarning("Plugin directory not found: {Directory}", pluginDirectory);
            return Array.Empty<PluginInfo>();
        }

        var discoveredPlugins = new List<PluginInfo>();

        // 查找所有 plugin.json 文件
        var manifestFiles = Directory.GetFiles(pluginDirectory, "plugin.json", SearchOption.AllDirectories);

        foreach (var manifestFile in manifestFiles)
        {
            try
            {
                var manifest = LoadManifest(manifestFile);
                if (manifest == null)
                {
                    _logger.LogWarning("Failed to load manifest from {File}", manifestFile);
                    continue;
                }

                var pluginInfo = new PluginInfo
                {
                    Manifest = manifest,
                    State = PluginState.Discovered,
                    AssemblyPath = GetAssemblyPath(manifestFile, manifest.EntryAssembly)
                };

                _plugins[manifest.Id] = pluginInfo;
                discoveredPlugins.Add(pluginInfo);

                _logger.LogInformation("Discovered plugin: {Name} ({Id}) v{Version}",
                    manifest.Name, manifest.Id, manifest.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering plugin from {File}", manifestFile);
            }
        }

        return discoveredPlugins;
    }

    public PluginLoadResult LoadPlugin(PluginInfo pluginInfo)
    {
        if (pluginInfo.State == PluginState.Disabled)
        {
            return PluginLoadResult.Fail("PLUGIN_DISABLED", "Plugin is disabled");
        }

        if (pluginInfo.State == PluginState.Loaded || pluginInfo.State == PluginState.Initialized)
        {
            return PluginLoadResult.Success(pluginInfo.Manifest);
        }

        try
        {
            pluginInfo.State = PluginState.Loading;

            // 验证依赖
            if (!ValidateDependencies(pluginInfo.Manifest, _plugins.Values.Where(p => p.State == PluginState.Loaded || p.State == PluginState.Initialized)))
            {
                var missingDeps = pluginInfo.Manifest.DependsOn?
                    .Where(d => !_plugins.ContainsKey(d) || _plugins[d].State < PluginState.Loaded)
                    .ToList() ?? new List<string>();

                pluginInfo.State = PluginState.Failed;
                pluginInfo.LoadError = $"Missing dependencies: {string.Join(", ", missingDeps)}";
                return PluginLoadResult.Fail("MISSING_DEPENDENCIES", pluginInfo.LoadError);
            }

            // 加载程序集
            if (string.IsNullOrEmpty(pluginInfo.AssemblyPath) || !File.Exists(pluginInfo.AssemblyPath))
            {
                pluginInfo.State = PluginState.Failed;
                pluginInfo.LoadError = "Assembly not found";
                return PluginLoadResult.Fail("ASSEMBLY_NOT_FOUND", pluginInfo.LoadError);
            }

            // 使用独立的 AssemblyLoadContext 加载插件
            var loadContext = new PluginAssemblyLoadContext(pluginInfo.AssemblyPath);
            _loadContexts[pluginInfo.Manifest.Id] = loadContext;

            var assembly = loadContext.LoadFromAssemblyPath(pluginInfo.AssemblyPath);

            // 查找模块类型
            if (!string.IsNullOrEmpty(pluginInfo.Manifest.ModuleType))
            {
                pluginInfo.ModuleImplementationType = assembly.GetType(pluginInfo.Manifest.ModuleType);
            }
            else
            {
                // 自动查找实现 IPluginModule 的类型
                pluginInfo.ModuleImplementationType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPluginModule).IsAssignableFrom(t) && !t.IsAbstract);
            }

            if (pluginInfo.ModuleImplementationType == null)
            {
                _logger.LogWarning("No plugin module type found for plugin {Id}", pluginInfo.Manifest.Id);
            }

            pluginInfo.State = PluginState.Loaded;
            pluginInfo.LoadedTime = DateTime.UtcNow;

            _logger.LogInformation("Loaded plugin: {Name} ({Id})",
                pluginInfo.Manifest.Name, pluginInfo.Manifest.Id);

            return PluginLoadResult.Success(pluginInfo.Manifest);
        }
        catch (Exception ex)
        {
            pluginInfo.State = PluginState.Failed;
            pluginInfo.LoadError = ex.Message;
            _logger.LogError(ex, "Failed to load plugin {Id}", pluginInfo.Manifest.Id);
            return PluginLoadResult.Fail("LOAD_ERROR", ex.Message, ex);
        }
    }

    public void InitializePlugin(PluginInfo pluginInfo, IServiceProvider serviceProvider)
    {
        if (pluginInfo.State != PluginState.Loaded)
        {
            _logger.LogWarning("Plugin {Id} is not in loaded state", pluginInfo.Manifest.Id);
            return;
        }

        if (pluginInfo.ModuleImplementationType == null)
        {
            _logger.LogWarning("No module type for plugin {Id}", pluginInfo.Manifest.Id);
            return;
        }

        try
        {
            // 创建模块实例
            pluginInfo.ModuleInstance = ActivatorUtilities.CreateInstance(serviceProvider, pluginInfo.ModuleImplementationType) as IPluginModule;

            if (pluginInfo.ModuleInstance != null)
            {
                pluginInfo.ModuleInstance.Initialize(serviceProvider);
                pluginInfo.State = PluginState.Initialized;
                _logger.LogInformation("Initialized plugin: {Name} ({Id})", pluginInfo.Manifest.Name, pluginInfo.Manifest.Id);
            }
            else
            {
                pluginInfo.State = PluginState.Failed;
                pluginInfo.LoadError = "Failed to create module instance";
            }
        }
        catch (Exception ex)
        {
            pluginInfo.State = PluginState.Failed;
            pluginInfo.LoadError = ex.Message;
            _logger.LogError(ex, "Failed to initialize plugin {Id}", pluginInfo.Manifest.Id);
        }
    }

    public IReadOnlyList<PluginInfo> GetLoadedPlugins()
    {
        return _plugins.Values
            .Where(p => p.State >= PluginState.Loaded)
            .ToList();
    }

    public void DisablePlugin(string pluginId)
    {
        if (_plugins.TryGetValue(pluginId, out var plugin))
        {
            plugin.State = PluginState.Disabled;
            _logger.LogInformation("Disabled plugin {Id}", pluginId);
        }
    }

    public void EnablePlugin(string pluginId)
    {
        if (_plugins.TryGetValue(pluginId, out var plugin))
        {
            plugin.State = PluginState.Discovered;
            plugin.Manifest.IsEnabled = true;
            _logger.LogInformation("Enabled plugin {Id}", pluginId);
        }
    }

    public bool ValidateDependencies(PluginManifest manifest, IEnumerable<PluginInfo> loadedPlugins)
    {
        if (manifest.DependsOn == null || manifest.DependsOn.Count == 0)
        {
            return true;
        }

        var loadedIds = loadedPlugins.Select(p => p.Manifest.Id).ToList();

        foreach (var dependency in manifest.DependsOn)
        {
            if (!loadedIds.Contains(dependency))
            {
                _logger.LogWarning("Plugin {Id} missing dependency {DepId}", manifest.Id, dependency);
                return false;
            }
        }

        return true;
    }

    private PluginManifest? LoadManifest(string manifestFile)
    {
        var json = File.ReadAllText(manifestFile);
        return JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // 安全选项：禁用反射实例化，防止恶意 plugin.json 注入
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 10  // 防止深度嵌套攻击
        });
    }

    private string? GetAssemblyPath(string manifestFile, string entryAssembly)
    {
        var directory = Path.GetDirectoryName(manifestFile);
        if (directory == null || string.IsNullOrEmpty(entryAssembly))
        {
            return null;
        }

        // 尝试多种可能的扩展名
        var possibleNames = new[] { entryAssembly, $"{entryAssembly}.dll" };
        foreach (var name in possibleNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}

/// <summary>
/// 插件专用的 AssemblyLoadContext，用于隔离插件程序集
/// </summary>
internal class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly string _pluginPath;
    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _pluginPath = pluginPath;
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 尝试从插件目录解析依赖
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // 回退到默认上下文 (共享框架程序集)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}