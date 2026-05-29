using System;
using System.Collections.Generic;

namespace CrestCreates.PluginSystem.Abstractions;

/// <summary>
/// 插件模块接口
/// 插件必须实现此接口以接入模块生命周期
/// </summary>
public interface IPluginModule
{
    /// <summary>
    /// 插件唯一标识
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// 插件名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 插件版本
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// 插件描述
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// 插件依赖的其他插件ID
    /// </summary>
    IReadOnlyList<string> Dependencies { get; }

    /// <summary>
    /// 插件初始化
    /// </summary>
    void Initialize(IServiceProvider serviceProvider);
}