using System;
using CrestCreates.PluginSystem.Abstractions;

namespace CrestCreates.PluginSystem.Models;

/// <summary>
/// 插件清单定义
/// 对应 plugin.json 文件
/// </summary>
public class PluginManifest
{
    /// <summary>
    /// 插件唯一标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 插件名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 插件版本 (语义版本)
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 插件描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 入口程序集名称 (不含 .dll)
    /// </summary>
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>
    /// 插件模块类型的全限定名
    /// </summary>
    public string? ModuleType { get; set; }

    /// <summary>
    /// 依赖的其他插件ID列表
    /// </summary>
    public List<string>? DependsOn { get; set; }

    /// <summary>
    /// 最低框架版本要求
    /// </summary>
    public string? MinFrameworkVersion { get; set; }

    /// <summary>
    /// 插件作者
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// 插件网站
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 插件加载结果
/// </summary>
public class PluginLoadResult
{
    public bool IsSuccess { get; init; }
    public PluginManifest? Manifest { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }

    public static PluginLoadResult Success(PluginManifest manifest) =>
        new() { IsSuccess = true, Manifest = manifest };

    public static PluginLoadResult Fail(string errorCode, string errorMessage, Exception? exception = null) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage, Exception = exception };
}

/// <summary>
/// 插件状态
/// </summary>
public enum PluginState
{
    /// <summary>
    /// 未加载
    /// </summary>
    Unloaded,

    /// <summary>
    /// 已发现
    /// </summary>
    Discovered,

    /// <summary>
    /// 加载中
    /// </summary>
    Loading,

    /// <summary>
    /// 已加载
    /// </summary>
    Loaded,

    /// <summary>
    /// 已初始化
    /// </summary>
    Initialized,

    /// <summary>
    /// 加载失败
    /// </summary>
    Failed,

    /// <summary>
    /// 已禁用
    /// </summary>
    Disabled
}

/// <summary>
/// 已加载的插件信息
/// </summary>
public class PluginInfo
{
    public PluginManifest Manifest { get; set; } = null!;
    public PluginState State { get; set; }
    public string? AssemblyPath { get; set; }
    public Type? ModuleImplementationType { get; set; }
    public IPluginModule? ModuleInstance { get; set; }
    public string? LoadError { get; set; }
    public DateTime? LoadedTime { get; set; }
}